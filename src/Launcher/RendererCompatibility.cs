using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SwgOldFlurryLauncher;

public static class RendererCompatibility
{
    public const string Hash="BD4E0B3A91F8A45E4C8FF000238B872043FEA9AFA7A7BA1D82E3CD18F7D65FF7";
    [StructLayout(LayoutKind.Sequential)] struct AppInfo {public uint Type;public IntPtr Next,Name;public uint Version;public IntPtr Engine;public uint EngineVersion,Api;}
    [StructLayout(LayoutKind.Sequential)] struct CreateInfo {public uint Type;public IntPtr Next;public uint Flags;public IntPtr App;public uint Layers;public IntPtr LayerNames;public uint Extensions;public IntPtr ExtensionNames;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Create(ref CreateInfo info,IntPtr allocator,out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Enumerate(IntPtr instance,ref uint count,IntPtr devices);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Properties(IntPtr device,IntPtr properties);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Destroy(IntPtr instance,IntPtr allocator);
    public static bool HasSupportedAmd()
    {
        // Ask the installed Vulkan driver for vendor/API support. Do not install the shim on NVIDIA/Intel-only PCs.
        if(!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),"vulkan-1.dll")))return false;
        if(!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory,"vulkan-1.dll"),out var library))return false;
        IntPtr instance=IntPtr.Zero,app=IntPtr.Zero,devices=IntPtr.Zero,properties=IntPtr.Zero;
        Destroy? destroy=null;
        try
        {
            var create=Marshal.GetDelegateForFunctionPointer<Create>(NativeLibrary.GetExport(library,"vkCreateInstance"));
            var enumerate=Marshal.GetDelegateForFunctionPointer<Enumerate>(NativeLibrary.GetExport(library,"vkEnumeratePhysicalDevices"));
            var getProperties=Marshal.GetDelegateForFunctionPointer<Properties>(NativeLibrary.GetExport(library,"vkGetPhysicalDeviceProperties"));
            destroy=Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library,"vkDestroyInstance"));
            app=Marshal.AllocHGlobal(Marshal.SizeOf<AppInfo>());Marshal.StructureToPtr(new AppInfo{Api=(1u<<22)|(1u<<12)},app,false);
            var info=new CreateInfo{Type=1,App=app};if(create(ref info,IntPtr.Zero,out instance)!=0)return false;
            uint count=0;if(enumerate(instance,ref count,IntPtr.Zero)!=0||count==0||count>64)return false;
            devices=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size));if(enumerate(instance,ref count,devices)!=0)return false;
            properties=Marshal.AllocHGlobal(4096);
            for(int i=0;i<count;i++)
            {
                getProperties(Marshal.ReadIntPtr(devices,i*IntPtr.Size),properties);
                uint api=unchecked((uint)Marshal.ReadInt32(properties));int vendor=Marshal.ReadInt32(properties,8);
                if(vendor==0x1002&&api>=((1u<<22)|(1u<<12)))return true;
            }
            return false;
        }
        catch(Exception e)when(e is EntryPointNotFoundException or DllNotFoundException or ArgumentException){return false;}
        finally
        {
            if(instance!=IntPtr.Zero)destroy?.Invoke(instance,IntPtr.Zero);
            if(app!=IntPtr.Zero)Marshal.FreeHGlobal(app);if(devices!=IntPtr.Zero)Marshal.FreeHGlobal(devices);if(properties!=IntPtr.Zero)Marshal.FreeHGlobal(properties);NativeLibrary.Free(library);
        }
    }
    public static bool Prepare(string client,string? dataRoot=null,bool? supportedAmd=null)
    {
        if(!(supportedAmd??HasSupportedAmd()))return false;
        string license=UpdateService.SafePath(client,"DXVK-LICENSE.txt");
        if(!File.Exists(license))
        {
            using var copyright=typeof(RendererCompatibility).Assembly.GetManifestResourceStream("OldFlurry.DXVK.LICENSE")??throw new IOException("DXVK license is missing.");
            using var file=new FileStream(license,FileMode.CreateNew);copyright.CopyTo(file);
        }
        string target=UpdateService.SafePath(client,"d3d9.dll");
        if(File.Exists(target)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))==Hash)return true;
        using var source=typeof(RendererCompatibility).Assembly.GetManifestResourceStream("OldFlurry.DXVK.d3d9.dll")??throw new IOException("The AMD compatibility renderer is missing from the launcher.");
        using var data=new MemoryStream();source.CopyTo(data);byte[] bytes=data.ToArray();
        if(Convert.ToHexString(SHA256.HashData(bytes))!=Hash)throw new IOException("AMD compatibility renderer checksum failed.");
        if(File.Exists(target))
        {
            string backup=Path.Combine(dataRoot??UpdateService.Data,"RuntimeBackups");UpdateService.RejectLinks(backup);Directory.CreateDirectory(backup);
            string originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
            string copy=Path.Combine(backup,"d3d9-"+Guid.NewGuid().ToString("N")+".dll");File.Copy(target,copy,false);
            if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copy)))!=originalHash)throw new IOException("Renderer backup failed verification.");
        }
        string temp=UpdateService.SafePath(client,".flurry-renderer-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllBytes(temp,bytes);File.Move(temp,target,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        return true;
    }
}
