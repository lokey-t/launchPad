Unicode true
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
Name "LaunchPad"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\LaunchPad"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
CRCCheck force
ManifestDPIAware true
Icon "${PROJECT}\Assets\app.ico"
VIProductVersion "${ASSEMBLY_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "LaunchPad Setup"
VIAddVersionKey /LANG=1033 "FileDescription" "LaunchPad Installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${ASSEMBLY_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "LaunchPad"
BrandingText " "
ShowInstDetails nevershow
AutoCloseWindow true
!define MUI_BGCOLOR FFFFFF
!define MUI_TEXTCOLOR 1F2328
!define MUI_ICON "${PROJECT}\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_CUSTOMFUNCTION_GUIINIT StyleWindow
Page custom Welcome LeaveWelcome
!insertmacro MUI_PAGE_INSTFILES
Page custom Finish FinishLeave
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"
!macro L name zh en
LangString ${name} ${LANG_SIMPCHINESE} "${zh}"
LangString ${name} ${LANG_ENGLISH} "${en}"
!macroend
!insertmacro L FreshTitle "一键安装，即刻开始" "One click. Ready to launch."
!insertmacro L UpdateTitle "更新你的启动台" "Update your LaunchPad"
!insertmacro L FreshDescription "选择安装位置，其余交给我们。$\r$\n已包含运行环境，无需联网下载。" "Choose a location and we'll handle the rest.$\r$\nRuntime included. No download required."
!insertmacro L UpdateDescription "已找到现有版本，将在原目录更新。$\r$\n应用、主题与备份都会保留。" "An existing version was found. Update in place.$\r$\nYour apps, themes and backups will stay."
!insertmacro L Location "安装位置" "Install location"
!insertmacro L Change "更改…" "Change..."
!insertmacro L Desktop "创建桌面快捷方式" "Create a desktop shortcut"
!insertmacro L Install "立即安装" "Install now"
!insertmacro L Update "立即更新" "Update now"
!insertmacro L Installing "正在解压、校验并安装，请稍候。" "Extracting, verifying and installing. Please wait."
!insertmacro L Ready "准备就绪" "You're all set"
!insertmacro L Complete "安装完成，可以开始使用了。" "Installed. You're ready to go."
!insertmacro L Launch "完成" "Done"
!insertmacro L LaunchCheck "启动 LaunchPad" "Launch LaunchPad"
!insertmacro L Failed "安装未完成。原文件已保留或恢复。请确认目录可写，退出占用文件的程序后重试。" "Installation did not finish. Original files are retained or restored. Check permissions and close apps using the files, then retry."
!insertmacro L Running "旧版本仍在运行，请从托盘退出 LaunchPad 后重试。" "Quit the previous LaunchPad from its tray menu and retry."
!insertmacro L InvalidLocation "请选择空文件夹或原安装目录，不能选择系统、配置或链接目录。" "Choose an empty folder or the original installation. System, configuration and linked folders cannot be used."
!insertmacro L Newer "已安装更新的版本，请使用更新的安装包。" "A newer version is installed. Use a newer setup package."
!insertmacro L Corrupt "文件校验失败，请重新下载安装包。" "File verification failed. Download setup again."
!insertmacro L Recovery "文件恢复未完成，请保留恢复目录：" "Recovery is incomplete. Keep this recovery folder:"
!insertmacro L ShortcutWarning "程序已安装，快捷方式未能创建。可从安装目录启动。" "Installed, but shortcuts could not be created. Launch from the install folder."
Var Dialog
Var DirectoryControl
Var TitleControl
Var DescriptionControl
Var DesktopControl
Var DesktopChoice
Var LaunchControl
Var TitleFont
Var BodyFont
Var TestMode
Var Mutex
Var Status
Var IconHandle
Var ButtonBitmap
Var Surface
Var Ink
Var Muted
Var Field
Var FillColor
Var ExplicitDirectory

; SetCtlColors accepts compile-time colors only; branch between literal palettes.
!macro ThemeInk control
  ${If} $Surface == "20242A"
    SetCtlColors ${control} F0F2F5 20242A
  ${Else}
    SetCtlColors ${control} 1F2328 FFFFFF
  ${EndIf}
!macroend
!macro ThemeMuted control
  ${If} $Surface == "20242A"
    SetCtlColors ${control} A3AAB4 20242A
  ${Else}
    SetCtlColors ${control} 777B83 FFFFFF
  ${EndIf}
!macroend
!macro ThemeAccent control
  ${If} $Surface == "20242A"
    SetCtlColors ${control} 66A9D5 20242A
  ${Else}
    SetCtlColors ${control} 2E6D99 FFFFFF
  ${EndIf}
!macroend
!macro ThemeField control
  ${If} $Surface == "20242A"
    SetCtlColors ${control} F0F2F5 292E35
  ${Else}
    SetCtlColors ${control} 1F2328 F5F5F7
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext current
  ${If} ${RunningX64}
    SetRegView 64
  ${EndIf}
  StrCpy $LANGUAGE ${LANG_ENGLISH}
  System::Call 'kernel32::GetUserDefaultUILanguage() i.r0'
  IntOp $0 $0 & 0x3ff
  ${If} $0 = 4
    StrCpy $LANGUAGE ${LANG_SIMPCHINESE}
  ${EndIf}
  StrCpy $TestMode "normal"
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} "$0" "/TEST" $1
  ${IfNot} ${Errors}
    StrCpy $TestMode "test"
  ${EndIf}
  ; NSIS removes /D from $CMDLINE; use the original OS command line to preserve overrides.
  System::Call 'kernel32::GetCommandLineW() w.r0'
  ${GetOptions} "$0" "/D=" $1
  StrCpy $ExplicitDirectory 1
  ${If} $1 == ""
    StrCpy $ExplicitDirectory 0
    ReadRegStr $1 HKCU "Software\LaunchPad\Installation" "InstallLocation"
    IfFileExists "$1\LaunchPad.exe" found 0
    ReadRegStr $2 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "LaunchPad"
    Call ParseExecutable
    IfFileExists "$1\LaunchPad.exe" found 0
    ReadRegStr $2 HKCU "Software\Classes\LaunchPad.Backup\shell\open\command" ""
    Call ParseExecutable
    IfFileExists "$1\LaunchPad.exe" found 0
    Goto detected
    found:
      StrCpy $INSTDIR "$1"
  ${EndIf}
  detected:
  System::Call 'kernel32::CreateMutexW(p0,i0,w "Local\LaunchPad.NSIS.Setup") p.r0 ?e'
  StrCpy $Mutex $0
  Pop $1
  ${If} $1 = 183
    MessageBox MB_OK "LaunchPad Setup is already running."
    Abort
  ${EndIf}
  StrCpy $DesktopChoice ${BST_CHECKED}
  InitPluginsDir
  StrCpy $Surface FFFFFF
  StrCpy $Ink 1F2328
  StrCpy $Muted 777B83
  StrCpy $Field F5F5F7
  StrCpy $FillColor 0xFFFFFF
  File /oname=$PLUGINSDIR\ReadPreferences.ps1 "${PROJECT}\Installer\NSIS\ReadPreferences.ps1"
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\ReadPreferences.ps1" -OutputPath "$PLUGINSDIR\preferences.ini"'
  Pop $0
  Pop $1
  ${If} $ExplicitDirectory == 0
    IfFileExists "$INSTDIR\LaunchPad.exe" runningDetected 0
    ReadINIStr $0 "$PLUGINSDIR\preferences.ini" "Preferences" "RunningDirectory"
    IfFileExists "$0\LaunchPad.exe" 0 runningDetected
    StrCpy $INSTDIR "$0"
    runningDetected:
  ${EndIf}
  ReadINIStr $0 "$PLUGINSDIR\preferences.ini" "Preferences" "Language"
  ${If} $0 == "en"
    StrCpy $LANGUAGE ${LANG_ENGLISH}
  ${ElseIf} $0 == "zh"
    StrCpy $LANGUAGE ${LANG_SIMPCHINESE}
  ${EndIf}
  ReadINIStr $0 "$PLUGINSDIR\preferences.ini" "Preferences" "Theme"
  ${GetParameters} $R0
  ${GetOptions} "$R0" "/THEME=" $R1
  ${If} $TestMode == "test"
  ${AndIf} $R1 == "Dark"
    StrCpy $0 "Dark"
  ${EndIf}
  ${If} $0 == "Dark"
    StrCpy $Surface 20242A
    StrCpy $Ink F0F2F5
    StrCpy $Muted A3AAB4
    StrCpy $Field 292E35
    StrCpy $FillColor 0x2A2420
  ${EndIf}
  ${GetParameters} $0
  ${GetOptions} "$0" "/LANG=" $1
  ${If} $1 == "en"
    StrCpy $LANGUAGE ${LANG_ENGLISH}
  ${ElseIf} $1 == "zh"
    StrCpy $LANGUAGE ${LANG_SIMPCHINESE}
  ${EndIf}
  File /oname=$PLUGINSDIR\app.ico "${PROJECT}\Assets\app.ico"
  CreateFont $TitleFont "Microsoft YaHei UI" 18 600
  CreateFont $BodyFont "Microsoft YaHei UI" 9 400
FunctionEnd

Function ParseExecutable
  StrCpy $1 ""
  StrCpy $3 $2 1
  ${If} $3 == '$\"'
    StrCpy $3 1
    loop:
      StrCpy $4 $2 1 $3
      StrCmp $4 "" parsed
      StrCmp $4 '$\"' parsed
      StrCpy $1 "$1$4"
      IntOp $3 $3 + 1
      Goto loop
    parsed:
  ${Else}
    StrCpy $1 $2
  ${EndIf}
  ${GetParent} "$1" $1
FunctionEnd

Function StyleWindow
  !insertmacro ThemeInk $HWNDPARENT
  System::Call 'dwmapi::DwmSetWindowAttribute(p $HWNDPARENT,i33,*i2,i4)'
  ${If} $Surface == "20242A"
    System::Call 'dwmapi::DwmSetWindowAttribute(p $HWNDPARENT,i20,*i1,i4)'
  ${EndIf}
  GetDlgItem $0 $HWNDPARENT 1
  SendMessage $0 ${WM_SETFONT} $BodyFont 1
  GetDlgItem $0 $HWNDPARENT 2
  SendMessage $0 ${WM_SETFONT} $BodyFont 1
FunctionEnd

Function Welcome
  nsDialogs::Create 1044
  Pop $Dialog
  !insertmacro ThemeInk $Dialog
  ${NSD_CreateIcon} 12u 8u 40u 40u ""
  Pop $0
  ${NSD_SetIcon} $0 "$PLUGINSDIR\app.ico" $IconHandle
  ${NSD_CreateLabel} 60u 10u 220u 24u "LaunchPad"
  Pop $0
  SendMessage $0 ${WM_SETFONT} $TitleFont 1
  !insertmacro ThemeAccent $0
  ${NSD_CreateLabel} 61u 36u 220u 14u "v${VERSION}"
  Pop $0
  !insertmacro ThemeMuted $0
  SendMessage $0 ${WM_SETFONT} $BodyFont 1
  ${NSD_CreateLabel} 12u 62u 280u 28u "$(FreshTitle)"
  Pop $TitleControl
  SendMessage $TitleControl ${WM_SETFONT} $TitleFont 1
  !insertmacro ThemeInk $TitleControl
  ${NSD_CreateLabel} 12u 95u 280u 32u "$(FreshDescription)"
  Pop $DescriptionControl
  !insertmacro ThemeMuted $DescriptionControl
  SendMessage $DescriptionControl ${WM_SETFONT} $BodyFont 1
  ${NSD_CreateLabel} 12u 126u 250u 12u "$(Location)"
  Pop $0
  !insertmacro ThemeMuted $0
  SendMessage $0 ${WM_SETFONT} $BodyFont 1
  ${NSD_CreateText} 12u 141u 224u 20u "$INSTDIR"
  Pop $DirectoryControl
  ${NSD_RemoveExStyle} $DirectoryControl ${WS_EX_CLIENTEDGE}
  System::Call 'user32::SetWindowPos(p $DirectoryControl,p0,i0,i0,i0,i0,i0x27)'
  SendMessage $DirectoryControl 0xD3 3 0x00070007
  System::Alloc 16
  Pop $0
  System::Call 'user32::GetClientRect(p $DirectoryControl,p r0)'
  System::Call '*$0(i,i,i.r1,i.r2)'
  System::Free $0
  System::Call 'gdi32::CreateRoundRectRgn(i0,i0,i r1,i r2,i8,i8) p.r0'
  System::Call 'user32::SetWindowRgn(p $DirectoryControl,p r0,i1)'
  !insertmacro ThemeField $DirectoryControl
  SendMessage $DirectoryControl ${WM_SETFONT} $BodyFont 1
  ${NSD_OnChange} $DirectoryControl DirectoryChanged
  ${NSD_CreateButton} 244u 141u 48u 20u "$(Change)"
  Pop $0
  SendMessage $0 ${WM_SETFONT} $BodyFont 1
  ${NSD_OnClick} $0 Browse
  ${NSD_CreateCheckbox} 12u 171u 280u 16u "$(Desktop)"
  Pop $DesktopControl
  ${If} $Surface == "20242A"
    System::Call 'uxtheme::SetWindowTheme(p $DesktopControl,w "",w "")'
  ${EndIf}
  !insertmacro ThemeInk $DesktopControl
  SendMessage $DesktopControl ${WM_SETFONT} $BodyFont 1
  ${NSD_SetState} $DesktopControl $DesktopChoice
  Call UpdateMode
  nsDialogs::Show
  ${NSD_FreeIcon} $IconHandle
FunctionEnd

Function DirectoryChanged
  Pop $0
  ${NSD_GetText} $DirectoryControl $INSTDIR
  Call UpdateMode
FunctionEnd

Function UpdateMode
  IfFileExists "$INSTDIR\LaunchPad.exe" update fresh
  update:
    ${NSD_SetText} $TitleControl "$(UpdateTitle)"
    ${NSD_SetText} $DescriptionControl "$(UpdateDescription)"
    GetDlgItem $0 $HWNDPARENT 1
    ${NSD_SetText} $0 "$(Update)"
    Call PaintPrimary
    Return
  fresh:
    ${NSD_SetText} $TitleControl "$(FreshTitle)"
    ${NSD_SetText} $DescriptionControl "$(FreshDescription)"
    GetDlgItem $0 $HWNDPARENT 1
    ${NSD_SetText} $0 "$(Install)"
    Call PaintPrimary
FunctionEnd

; Keep a native accessible button (keyboard/focus/click behavior) and paint its image in our palette.
Function PaintPrimary
  GetDlgItem $0 $HWNDPARENT 1
  System::Alloc 16
  Pop $1
  System::Call 'user32::GetClientRect(p r0,p r1)'
  System::Call '*$1(i,i,i.r8,i.r9)'
  IntOp $8 $8 - 4
  IntOp $9 $9 - 4
  System::Call '*$1(i0,i0,i r8,i r9)'
  System::Call 'user32::GetDC(p r0) p.r2'
  System::Call 'gdi32::CreateCompatibleDC(p r2) p.r3'
  System::Call 'gdi32::CreateCompatibleBitmap(p r2,i r8,i r9) p.r4'
  System::Call 'gdi32::SelectObject(p r3,p r4) p.r5'
  System::Call 'gdi32::CreateSolidBrush(i $FillColor) p.r6'
  System::Call 'user32::FillRect(p r3,p r1,p r6)'
  System::Call 'gdi32::DeleteObject(p r6)'
  System::Call 'gdi32::CreateSolidBrush(i0x996D2E) p.r6'
  System::Call 'gdi32::SelectObject(p r3,p r6) p.r7'
  System::Call 'gdi32::GetStockObject(i8) p.s'
  Pop $R0
  System::Call 'gdi32::SelectObject(p r3,p R0) p.R1'
  System::Call 'gdi32::RoundRect(p r3,i0,i0,i r8,i r9,i10,i10)'
  System::Call 'gdi32::SelectObject(p r3,p $BodyFont) p.R2'
  System::Call 'gdi32::SetBkMode(p r3,i1)'
  System::Call 'gdi32::SetTextColor(p r3,i0xFFFFFF)'
  ${NSD_GetText} $0 $R3
  System::Call 'user32::DrawTextW(p r3,w R3,i-1,p r1,i0x25)'
  System::Call 'gdi32::SelectObject(p r3,p R2)'
  System::Call 'gdi32::SelectObject(p r3,p R1)'
  System::Call 'gdi32::SelectObject(p r3,p r7)'
  System::Call 'gdi32::SelectObject(p r3,p r5)'
  System::Call 'gdi32::DeleteObject(p r6)'
  System::Call 'gdi32::DeleteDC(p r3)'
  System::Call 'user32::ReleaseDC(p r0,p r2)'
  System::Free $1
  ${NSD_AddStyle} $0 ${BS_BITMAP}
  SendMessage $0 ${BM_SETIMAGE} ${IMAGE_BITMAP} $4
  System::Call 'gdi32::DeleteObject(p $ButtonBitmap)'
  StrCpy $ButtonBitmap $4
FunctionEnd

Function Browse
  Pop $0
  nsDialogs::SelectFolderDialog "$(Location)" "$INSTDIR"
  Pop $0
  StrCmp $0 "error" done
  ${NSD_SetText} $DirectoryControl "$0"
  done:
FunctionEnd

Function LeaveWelcome
  ${NSD_GetText} $DirectoryControl $INSTDIR
  ${NSD_GetState} $DesktopControl $DesktopChoice
  StrCmp $INSTDIR "" 0 +3
    MessageBox MB_OK "$(InvalidLocation)"
    Abort
FunctionEnd

Section "Install"
  SetOutPath "$PLUGINSDIR\payload"
  File /r "${PAYLOAD}\*"
  DetailPrint "$(Installing)"
  nsExec::ExecToStack '"$PLUGINSDIR\payload\LaunchPad.Setup.Worker.exe" "$INSTDIR" "$PLUGINSDIR\status.txt" "$DesktopChoice" "$TestMode"'
  Pop $0
  Pop $1
  StrCpy $Status ""
  FileOpen $1 "$PLUGINSDIR\status.txt" r
  IfErrors statusDone 0
    FileSeek $1 2 SET
    FileReadUTF16LE $1 $Status
    FileClose $1
  statusDone:
  ${If} $0 != 0
    StrCpy $2 "$(Failed)"
    ${If} $Status == "ApplicationStillRunning"
      StrCpy $2 "$(Running)"
    ${ElseIf} $Status == "InvalidDirectory"
    ${OrIf} $Status == "ChooseEmptyDirectory"
    ${OrIf} $Status == "LinkedPath"
      StrCpy $2 "$(InvalidLocation)"
    ${ElseIf} $Status == "NewerVersionInstalled"
      StrCpy $2 "$(Newer)"
    ${ElseIf} $Status == "InvalidPayload"
    ${OrIf} $Status == "VersionMismatch"
      StrCpy $2 "$(Corrupt)"
    ${EndIf}
    StrCpy $3 $Status 15
    ${If} $3 == "RollbackFailed:"
      StrCpy $2 "$(Recovery)$\r$\n$Status"
    ${EndIf}
    IfSilent +2 0
      MessageBox MB_OK|MB_ICONEXCLAMATION "$2"
    SetErrorLevel 1
    Abort
  ${EndIf}
  SetErrorLevel 0
SectionEnd

Function Finish
  nsDialogs::Create 1044
  Pop $Dialog
  !insertmacro ThemeInk $Dialog
  ${NSD_CreateIcon} 12u 16u 48u 48u ""
  Pop $0
  ${NSD_SetIcon} $0 "$PLUGINSDIR\app.ico" $IconHandle
  ${NSD_CreateLabel} 12u 78u 280u 30u "$(Ready)"
  Pop $0
  SendMessage $0 ${WM_SETFONT} $TitleFont 1
  !insertmacro ThemeInk $0
  ${NSD_CreateLabel} 12u 116u 280u 32u "$(Complete)"
  Pop $0
  !insertmacro ThemeMuted $0
  SendMessage $0 ${WM_SETFONT} $BodyFont 1
  ${If} $Status == "ShortcutWarning"
    ${NSD_SetText} $0 "$(ShortcutWarning)"
  ${EndIf}
  ${NSD_CreateCheckbox} 12u 171u 280u 18u "$(LaunchCheck)"
  Pop $LaunchControl
  ${If} $Surface == "20242A"
    System::Call 'uxtheme::SetWindowTheme(p $LaunchControl,w "",w "")'
  ${EndIf}
  !insertmacro ThemeInk $LaunchControl
  SendMessage $LaunchControl ${WM_SETFONT} $BodyFont 1
  ${NSD_SetState} $LaunchControl ${BST_CHECKED}
  ; On completion only one action remains. Align it with the right edge of the footer.
  GetDlgItem $0 $HWNDPARENT 2
  System::Alloc 16
  Pop $1
  System::Call 'user32::GetWindowRect(p r0,p r1)'
  System::Call 'user32::MapWindowPoints(p0,p $HWNDPARENT,p r1,i2)'
  System::Call '*$1(i.r2,i.r3,i.r4,i.r5)'
  System::Free $1
  ShowWindow $0 ${SW_HIDE}
  GetDlgItem $0 $HWNDPARENT 3
  ShowWindow $0 ${SW_HIDE}
  IntOp $4 $4 - $2
  IntOp $5 $5 - $3
  GetDlgItem $0 $HWNDPARENT 1
  System::Call 'user32::MoveWindow(p r0,i r2,i r3,i r4,i r5,i1)'
  GetDlgItem $0 $HWNDPARENT 1
  ${NSD_SetText} $0 "$(Launch)"
  Call PaintPrimary
  nsDialogs::Show
  ${NSD_FreeIcon} $IconHandle
FunctionEnd

Function FinishLeave
  ${NSD_GetState} $LaunchControl $0
  ${If} $0 = ${BST_CHECKED}
  ${AndIf} $TestMode != "test"
    SetOutPath "$INSTDIR"
    Exec '"$INSTDIR\LaunchPad.exe"'
  ${EndIf}
FunctionEnd

Function .onGUIEnd
  System::Call 'kernel32::CloseHandle(p $Mutex)'
  System::Call 'gdi32::DeleteObject(p $TitleFont)'
  System::Call 'gdi32::DeleteObject(p $BodyFont)'
  System::Call 'gdi32::DeleteObject(p $ButtonBitmap)'
FunctionEnd
