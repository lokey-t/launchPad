"""Publish LaunchPad release assets using credentials already stored by Git.
Requires explicit --execute for mutations. Never prints or stores credentials.
"""
import argparse, concurrent.futures, hashlib, json, os, pathlib, re, subprocess, sys, time, http.client
import urllib.request, urllib.parse, urllib.error, uuid

REPO = "lokey-t/launchPad"
class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        result = super().redirect_request(req, fp, code, msg, headers, newurl)
        if urllib.parse.urlparse(newurl).scheme != "https":
            raise RuntimeError("Refusing non-HTTPS redirect")
        if result and urllib.parse.urlparse(req.full_url).netloc != urllib.parse.urlparse(newurl).netloc:
            result.remove_header("Authorization")
        return result

def sha(path):
    with open(path,"rb") as stream:return hashlib.file_digest(stream,"sha256").hexdigest()

class Publisher:
    def __init__(self, host, version):
        self.host=host; self.tag="v"+version; self.version=version
        domain="github.com" if host=="github" else "gitee.com"
        self.base=("https://api.github.com" if host=="github" else "https://gitee.com/api/v5")+"/repos/"+REPO
        env=os.environ.copy();env["GIT_TERMINAL_PROMPT"]="0";env["GCM_INTERACTIVE"]="Never"
        result=subprocess.run(["git","credential","fill"],input=f"protocol=https\nhost={domain}\n\n",text=True,capture_output=True,env=env)
        values=dict(x.split("=",1) for x in result.stdout.splitlines() if "=" in x)
        self.token=os.environ.get("GITHUB_TOKEN" if host=="github" else "GITEE_TOKEN") or values.get("password")
        if not self.token:raise RuntimeError(f"No existing credential for {host}")
        self.opener=urllib.request.build_opener(SafeRedirect())

    def request(self, path, method="GET", data=None, raw=None, content_type=None):
        url=path if path.startswith("https://") else self.base+path
        allowed={"api.github.com","uploads.github.com"} if self.host=="github" else {"gitee.com"}
        if urllib.parse.urlparse(url).hostname not in allowed:raise RuntimeError("Unexpected API host")
        headers={"User-Agent":"LaunchPad-Release","Authorization":"Bearer "+self.token,"Accept":"application/json"}
        if data is not None:raw=json.dumps(data).encode();content_type="application/json"
        if content_type:headers["Content-Type"]=content_type
        try:
            with self.opener.open(urllib.request.Request(url,data=raw,headers=headers,method=method),timeout=300) as response:
                body=response.read();return json.loads(body) if body else None
        except urllib.error.HTTPError as error:
            # Error bodies can contain request details. Print only a redacted, bounded message.
            message=error.read().decode("utf-8","replace").replace(self.token,"[REDACTED]")[:450]
            raise RuntimeError(f"{self.host} {method} HTTP {error.code}: {message}") from None

    def release(self):
        try:return self.request("/releases/tags/"+self.tag)
        except RuntimeError as e:
            if "HTTP 404:" in str(e):
                # GitHub's tag endpoint does not always expose an unpublished draft.
                for page in range(1,20):
                    batch=self.request(f"/releases?per_page=100&page={page}")
                    for release in batch:
                        if release.get("tag_name")==self.tag:return release
                    if len(batch)<100:return None
                raise RuntimeError("Release pagination did not terminate")
            raise

    def assets(self, release):
        endpoint=f"/releases/{release['id']}/"+("assets" if self.host=="github" else "attach_files")
        results=[]
        for page in range(1,100):
            batch=self.request(endpoint+f"?per_page=100&page={page}")
            if not isinstance(batch,list):raise RuntimeError("Unexpected asset listing")
            results.extend(batch)
            if len(batch)<100:return results
        raise RuntimeError("Asset pagination did not terminate")

    def download_hash(self, asset):
        if self.host=="github":
            url=asset["url"];headers={"User-Agent":"LaunchPad-Release","Accept":"application/octet-stream","Authorization":"Bearer "+self.token}
        else:
            url=asset.get("browser_download_url") or asset.get("url")
            # Attachment listing may omit the public URL. Use the documented download route.
            url=self.base+f"/releases/{self.release_id}/attach_files/{asset['id']}/download"
            headers={"User-Agent":"LaunchPad-Release","Authorization":"Bearer "+self.token}
        for attempt in range(3):
            try:
                with self.opener.open(urllib.request.Request(url,headers=headers),timeout=300) as response:
                    return hashlib.file_digest(response,"sha256").hexdigest()
            except urllib.error.HTTPError as error:
                if error.code not in (408,429,500,502,503,504) or attempt==2:raise
            except (OSError,http.client.HTTPException):
                if attempt==2:raise
            time.sleep(attempt+1)

    def matches(self, asset, path):
        digest=asset.get("digest","") or ""
        if digest.startswith("sha256:"):return digest[7:].lower()==sha(path)
        return self.download_hash(asset)==sha(path)

    def upload(self, release, path):
        if self.host=="github":
            return self.request(f"https://uploads.github.com/repos/{REPO}/releases/{release['id']}/assets?name="+urllib.parse.quote(path.name),method="POST",raw=path.read_bytes(),content_type="application/octet-stream")
        boundary="LaunchPad"+uuid.uuid4().hex
        raw=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{path.name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()+path.read_bytes()+f"\r\n--{boundary}--\r\n".encode()
        return self.request(f"/releases/{release['id']}/attach_files",method="POST",raw=raw,content_type="multipart/form-data; boundary="+boundary)

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("host",choices=["github","gitee"]);p.add_argument("mode",choices=["prepare","upload","verify","publish"])
    p.add_argument("--version",required=True);p.add_argument("--assets",type=pathlib.Path);p.add_argument("--notes",type=pathlib.Path,default=pathlib.Path("RELEASE_NOTES.md"))
    p.add_argument("--commit");p.add_argument("--execute",action="store_true");p.add_argument("--workers",type=int,default=3)
    p.add_argument("--external-installer",action="store_true",help="Gitee only: verify the oversized setup on GitHub and link it from release notes")
    p.add_argument("--metadata-only",action="store_true",help="Gitee verify/publish only: check attachment names and sizes when bulk downloads are blocked; does not claim byte verification")
    a=p.parse_args()
    if not re.fullmatch(r"\d+\.\d+(\.\d+){0,2}",a.version):p.error("Use a numeric version")
    if a.mode!="verify" and not a.execute:p.error("Mutations require --execute and prior user authorization")
    if a.metadata_only and (a.host!="gitee" or a.mode not in ("verify","publish")):p.error("Metadata-only mode is limited to Gitee verify/publish")
    pub=Publisher(a.host,a.version);release=pub.release()
    if a.mode=="prepare":
        if not a.commit or not re.fullmatch("[a-f0-9]{40}",a.commit):p.error("--commit must be a full, already pushed commit SHA")
        remote=pub.request("/git/ref/tags/"+pub.tag) if a.host=="github" else None
        # A lightweight release tag must identify exactly the requested source commit.
        if remote and remote.get("object",{}).get("sha")!=a.commit:raise RuntimeError("Remote tag does not match requested commit")
        if release:print("Release already exists:",release["id"]);return
        data={"tag_name":pub.tag,"target_commitish":a.commit,"name":"LaunchPad "+pub.tag,"body":a.notes.read_text(encoding="utf-8"),"prerelease":True}
        if a.host=="github":data.update(draft=True,prerelease=False)
        release=pub.request("/releases",method="POST",data=data)
        print("Prepared",a.host,"release",release["id"],flush=True);return
    if not release:raise RuntimeError("Prepare the release first")
    pub.release_id=release["id"]
    if not a.assets or not a.assets.is_dir():p.error("--assets is required")
    files=sorted(x for x in a.assets.iterdir() if x.is_file())
    sums=a.assets/f"LaunchPad-v{a.version}-sha256.txt"
    expected={}
    for line in sums.read_text().splitlines():
        digest,name=line.split("  ",1);expected[name]=digest
    if {f.name for f in files}!={*expected,sums.name}:raise RuntimeError("Unexpected or missing local asset")
    for f in files:
        if f.name in expected and sha(f)!=expected[f.name]:raise RuntimeError("Local checksum mismatch: "+f.name)
    if a.external_installer:
        if a.host!="gitee":p.error("--external-installer is only supported for Gitee")
        installer=a.assets/f"LaunchPad-v{a.version}-win-x64-setup.exe"
        link=f"https://github.com/{REPO}/releases/download/v{a.version}/{installer.name}"
        if link not in a.notes.read_text(encoding="utf-8"):raise RuntimeError("Release notes must disclose and link the external installer")
        github=Publisher("github",a.version);github_release=github.release()
        if not github_release:raise RuntimeError("GitHub release missing")
        asset=next((x for x in github.assets(github_release) if x["name"]==installer.name),None)
        if not asset or not github.matches(asset,installer):raise RuntimeError("External installer is not verified on GitHub")
        if a.mode=="publish" and (github_release.get("draft") or github_release.get("prerelease")):raise RuntimeError("Publish the GitHub installer before linking it from the stable Gitee release")
        files=[f for f in files if f!=installer]
        print("Verified external installer on GitHub; Gitee file limit fallback enabled",flush=True)
    existing={x["name"]:x for x in pub.assets(release)}
    def work(path):
        asset=existing.get(path.name)
        if asset is not None:
            if a.metadata_only:
                if asset.get("size")!=path.stat().st_size:raise RuntimeError("Remote attachment size differs: "+path.name)
            elif not pub.matches(asset,path):raise RuntimeError("Existing asset differs; refusing replacement: "+path.name)
        elif a.mode=="upload":
            asset=pub.upload(release,path)
            if not pub.matches(asset,path):raise RuntimeError("Uploaded bytes differ: "+path.name)
        else:raise RuntimeError("Missing remote asset: "+path.name)
        return path.name
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1,min(4,a.workers))) as pool:
        futures={pool.submit(work,f):f.name for f in files}
        for i,future in enumerate(concurrent.futures.as_completed(futures),1):
            try:name=future.result()
            except Exception as error:
                for queued in futures:queued.cancel()
                raise RuntimeError(f"Asset {futures[future]}: {error}") from None
            if i%20==0 or name.endswith((".exe",".zip",".txt")):print(a.host,f"{i}/{len(files)}",("metadata checked" if a.metadata_only else "verified"),name,flush=True)
    if a.mode=="publish":
        data={"body":a.notes.read_text(encoding="utf-8"),"prerelease":False}
        if a.host=="github":data.update(draft=False,make_latest="true")
        else:data.update(tag_name=release["tag_name"],name=release["name"])
        pub.request(f"/releases/{release['id']}",method="PATCH",data=data)
    print("COMPLETE",a.host,a.mode,len(files),"assets",("(names/sizes only; remote byte verification incomplete)" if a.metadata_only else "(remote hashes verified)"),flush=True)

if __name__=="__main__":
    try:main()
    except Exception as e:print(type(e).__name__+": "+str(e),file=sys.stderr);sys.exit(1)
