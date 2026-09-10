using System.Runtime.InteropServices;
using System.Numerics;

namespace LaunchPad.Services;

// Windows Runtime ABI bindings keep the renderer independent of a WinUI runtime.
// The graph contains only Gaussian blur: no tint, luminosity, saturation or opacity effect.
internal sealed class NativeGaussianBackdrop : IDisposable
{
    private readonly List<IntPtr> _owned=new();
    private static IntPtr _queue;
    private IntPtr _visual,_properties,_compositor,_target;
    private int _width=-1,_height=-1;
    private float _opacity=-1;
    private float _radius=-1;
    public NativeGaussianBackdrop(IntPtr hwnd)
    {
        try
        {
            if(_queue==IntPtr.Zero)
            {
                var options=new QueueOptions { Size=12,ThreadType=2,ApartmentType=2 };
                Check(CreateDispatcherQueueController(options,out _queue));
            }
            using var name=new HString("Windows.UI.Composition.Compositor");
            Check(RoActivateInstance(name.Value,out var instance)); Own(instance); _compositor=instance;
            var compositor=Own(Query(instance,"b403ca50-7f8c-4e83-985f-cc45060036d8"));
            var desktop=Own(Query(instance,"29e691fa-4567-4dca-b319-d0f207eb6807"));
            Check(Method<CreateTarget>(desktop,3)(desktop,hwnd,false,out var target)); Own(target);
            var targetInterface=Own(Query(target,"a1bea8ba-d726-4663-8129-6b5e7927ffa6")); _target=targetInterface;
            Check(Method<CreateObject>(compositor,22)(compositor,out var sprite)); Own(sprite);
            _visual=Own(Query(sprite,"117e202d-a859-4c89-873b-c2aa566788e3"));
            Check(Method<SetObject>(targetInterface,7)(targetInterface,_visual));
            var compositor2=Own(Query(instance,"735081dc-5e24-45da-a38f-e32cc349a9a0"));
            Check(Method<CreateObject>(compositor2,8)(compositor2,out var backdrop)); Own(backdrop);
            var sourceFactory=Own(Factory("Windows.UI.Composition.CompositionEffectSourceParameter","b3d9f276-aba3-4724-acf3-d0397464db1c"));
            using var sourceName=new HString("Backdrop");
            Check(Method<CreateWithArg>(sourceFactory,6)(sourceFactory,sourceName.Value,out var parameter)); Own(parameter);
            var source=Own(Query(parameter,"2d8f9ddc-4339-4eb9-9216-f9deb75658a2"));
            var propertyFactory=Own(Factory("Windows.Foundation.PropertyValue","629bdbc8-d932-4ff4-96b9-8d96c5c1e858"));
            var effect=new GaussianEffect(source,propertyFactory);
            var paths=new StringSequence("Blur.StandardDeviation");
            var effectPtr=Marshal.GetComInterfaceForObject(effect,typeof(IGraphicsEffectAbi));
            var pathsPtr=Marshal.GetComInterfaceForObject(paths,typeof(IStringIterableAbi));
            IntPtr factory;
            try { Check(Method<CreateFactory>(compositor,12)(compositor,effectPtr,pathsPtr,out factory)); }
            finally { Marshal.Release(effectPtr); Marshal.Release(pathsPtr); }
            Own(factory);
            Check(Method<CreateObject>(factory,6)(factory,out var brush)); Own(brush);
            Check(Method<SetSource>(brush,7)(brush,sourceName.Value,backdrop));
            var brushInterface=Own(Query(brush,"ab0d7608-30c0-40e9-b568-b60a6bd1fb46"));
            Check(Method<SetObject>(sprite,7)(sprite,brushInterface));
            var objectInterface=Own(Query(brush,"bcb4ad45-7609-4550-934f-16002a68fded"));
            Check(Method<CreateObject>(objectInterface,8)(objectInterface,out _properties)); Own(_properties);
        }
        catch { Dispose(); throw; }
    }
    public void Update(int width,int height,float radius,float opacity)
    {
        if(width!=_width || height!=_height)
        { Check(Method<SetVector>(_visual,36)(_visual,new Vector2(width,height))); _width=width; _height=height; }
        if(Math.Abs(opacity-_opacity)>.0001)
        { Check(Method<SetFloat>(_visual,23)(_visual,opacity)); _opacity=opacity; }
        if(Math.Abs(radius-_radius)<.01) return;
        using var name=new HString("Blur.StandardDeviation");
        Check(Method<InsertFloat>(_properties,10)(_properties,name.Value,radius)); _radius=radius;
    }
    public void Dispose()
    {
        if(_target!=IntPtr.Zero) Method<SetObject>(_target,7)(_target,IntPtr.Zero);
        if(_compositor!=IntPtr.Zero)
        {
            var iid=new Guid("30d5a829-7fa4-4026-83bb-d75bae4ea99e");
            if(Marshal.QueryInterface(_compositor,ref iid,out var close)==0)
            { try { Method<CloseObject>(close,6)(close); } finally { Marshal.Release(close); } }
        }
        for(int i=_owned.Count-1;i>=0;i--) Marshal.Release(_owned[i]);
        _target=_compositor=IntPtr.Zero;
        _owned.Clear();
    }
    private IntPtr Own(IntPtr value) { _owned.Add(value); return value; }
    internal static T Method<T>(IntPtr obj,int slot) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
    internal static void Check(int hr) { if(hr<0) Marshal.ThrowExceptionForHR(hr); }
    internal static IntPtr Query(IntPtr obj,string guid) { var iid=new Guid(guid); Check(Marshal.QueryInterface(obj,ref iid,out var result)); return result; }
    private static IntPtr Factory(string name,string guid) { using var text=new HString(name); var iid=new Guid(guid); Check(RoGetActivationFactory(text.Value,ref iid,out var factory)); return factory; }
    internal sealed class HString : IDisposable
    {
        public IntPtr Value;
        public HString(string value) { Check(WindowsCreateString(value,value.Length,out Value)); }
        public void Dispose() { WindowsDeleteString(Value); }
        public static IntPtr Copy(string value) { Check(WindowsCreateString(value,value.Length,out var text)); return text; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size,ThreadType,ApartmentType; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options,out IntPtr controller);
    [DllImport("combase.dll")] private static extern int RoActivateInstance(IntPtr name,out IntPtr instance);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)] private static extern int WindowsCreateString(string value,int length,out IntPtr text);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr text);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CloseObject(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTarget(IntPtr self,IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)] bool topmost,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateObject(IntPtr self,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateWithArg(IntPtr self,IntPtr argument,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateFactory(IntPtr self,IntPtr effect,IntPtr properties,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetObject(IntPtr self,IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetSource(IntPtr self,IntPtr name,IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetFloat(IntPtr self,float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetVector(IntPtr self,Vector2 value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InsertFloat(IntPtr self,IntPtr name,float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateFloatValue(IntPtr self,float value,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateUIntValue(IntPtr self,uint value,out IntPtr result);
}

[ComVisible(true),Guid("af86e2e0-b12d-4c6a-9c5a-d7aa65101e90"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInspectableAbi
{
    [PreserveSig] int GetIids(out uint count,out IntPtr ids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr name);
    [PreserveSig] int GetTrustLevel(out int trust);
}
[ComVisible(true),Guid("2d8f9ddc-4339-4eb9-9216-f9deb75658a2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IGraphicsEffectSourceAbi
{
    [PreserveSig] int GetIids(out uint count,out IntPtr ids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr name);
    [PreserveSig] int GetTrustLevel(out int trust);
}
[ComVisible(true),Guid("cb51c0ce-8fe6-4636-b202-861faa07d8f3"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IGraphicsEffectAbi
{
    [PreserveSig] int GetIids(out uint count,out IntPtr ids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr name);
    [PreserveSig] int GetTrustLevel(out int trust);
    [PreserveSig] int GetName(out IntPtr name);
    [PreserveSig] int SetName(IntPtr name);
}
[ComVisible(true),Guid("2FC57384-A068-44D7-A331-30982FCF7177"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IGraphicsEffectInteropAbi
{
    [PreserveSig] int GetEffectId(out Guid id);
    [PreserveSig] int GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name,out uint index,out uint mapping);
    [PreserveSig] int GetPropertyCount(out uint count);
    [PreserveSig] int GetProperty(uint index,out IntPtr value);
    [PreserveSig] int GetSource(uint index,out IntPtr source);
    [PreserveSig] int GetSourceCount(out uint count);
}
[ComVisible(true),ClassInterface(ClassInterfaceType.None)]
public sealed class GaussianEffect : IInspectableAbi,IGraphicsEffectSourceAbi,IGraphicsEffectAbi,IGraphicsEffectInteropAbi
{
    private readonly IntPtr _source,_factory;
    public GaussianEffect(IntPtr source,IntPtr factory) { _source=source; _factory=factory; }
    public int GetIids(out uint count,out IntPtr ids) { count=0; ids=IntPtr.Zero; return 0; }
    public int GetRuntimeClassName(out IntPtr name) { name=NativeGaussianBackdrop.HString.Copy("LaunchPad.GaussianEffect"); return 0; }
    public int GetTrustLevel(out int trust) { trust=0; return 0; }
    public int GetName(out IntPtr name) { name=NativeGaussianBackdrop.HString.Copy("Blur"); return 0; }
    public int SetName(IntPtr name)=>0;
    public int GetEffectId(out Guid id) { id=new Guid("1feb6d69-2fe6-4ac9-8c58-1d7f93e7a6a5"); return 0; }
    public int GetNamedPropertyMapping(string name,out uint index,out uint mapping) { index=0; mapping=1; return name=="StandardDeviation"?0:unchecked((int)0x80070057); }
    public int GetPropertyCount(out uint count) { count=3; return 0; }
    public int GetProperty(uint index,out IntPtr value)
    {
        value=IntPtr.Zero;
        if(index==0) return NativeGaussianBackdrop.Method<NativeGaussianBackdrop.CreateFloatValue>(_factory,14)(_factory,0,out value);
        if(index<=2) return NativeGaussianBackdrop.Method<NativeGaussianBackdrop.CreateUIntValue>(_factory,11)(_factory,1,out value);
        return unchecked((int)0x80070057);
    }
    public int GetSource(uint index,out IntPtr source) { source=IntPtr.Zero; if(index!=0) return unchecked((int)0x80070057); Marshal.AddRef(_source); source=_source; return 0; }
    public int GetSourceCount(out uint count) { count=1; return 0; }
}
[ComVisible(true),Guid("e2fcc7c1-3bfc-5a0b-b2b0-72e769d1cb7e"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStringIterableAbi
{
    [PreserveSig] int GetIids(out uint count,out IntPtr ids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr name);
    [PreserveSig] int GetTrustLevel(out int trust);
    [PreserveSig] int First(out IntPtr iterator);
}
[ComVisible(true),Guid("8c304ebb-6615-50a4-8829-879ecd443236"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStringIteratorAbi
{
    [PreserveSig] int GetIids(out uint count,out IntPtr ids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr name);
    [PreserveSig] int GetTrustLevel(out int trust);
    [PreserveSig] int Current(out IntPtr value);
    [PreserveSig] int HasCurrent(out byte value);
    [PreserveSig] int MoveNext(out byte value);
    [PreserveSig] int GetMany(uint capacity,IntPtr values,out uint actual);
}
[ComVisible(true),ClassInterface(ClassInterfaceType.None)]
public sealed class StringSequence : IInspectableAbi,IStringIterableAbi,IStringIteratorAbi
{
    private readonly string _value;
    private bool _current=true;
    public StringSequence(string value) { _value=value; }
    public int GetIids(out uint count,out IntPtr ids) { count=0; ids=IntPtr.Zero; return 0; }
    public int GetRuntimeClassName(out IntPtr name) { name=IntPtr.Zero; return 0; }
    public int GetTrustLevel(out int trust) { trust=0; return 0; }
    public int First(out IntPtr iterator) { iterator=Marshal.GetComInterfaceForObject(new StringSequence(_value),typeof(IStringIteratorAbi)); return 0; }
    public int Current(out IntPtr value) { value=NativeGaussianBackdrop.HString.Copy(_value); return 0; }
    public int HasCurrent(out byte value) { value=(byte)(_current?1:0); return 0; }
    public int MoveNext(out byte value) { _current=false; value=0; return 0; }
    public int GetMany(uint capacity,IntPtr values,out uint actual) { actual=0; if(capacity>0 && _current) { Marshal.WriteIntPtr(values,NativeGaussianBackdrop.HString.Copy(_value)); actual=1; _current=false; } return 0; }
}
