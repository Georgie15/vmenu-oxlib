"""Exercise production vMenu save serialization with in-memory KVP adapters (.NET 9 SDK)."""
from pathlib import Path
import argparse, re, subprocess, tempfile
from xml.sax.saxutils import escape
parser=argparse.ArgumentParser()
parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[2])
args=parser.parse_args();root=args.root.resolve()
storage=(root/'vMenu/StorageManager.cs').read_text(encoding='utf-8-sig')
common=(root/'vMenu/CommonFunctions.cs').read_text(encoding='utf-8-sig')
def member(source, signature):
    return re.search(r'^        '+re.escape(signature)+r'.*?^        };?$',source,re.M|re.S)[0]
fields=member(common,'public struct VehicleInfo')
save=member(storage,'public static bool SaveVehicleInfo(')
get=member(storage,'public static VehicleInfo GetSavedVehicleInfo(')
program=r"""
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public static class Program {
    private static readonly Dictionary<string,string> Kvp = new Dictionary<string,string>();
    private static string GetResourceKvpString(string key) => Kvp.TryGetValue(key,out var json) ? json : "";
    private static void SetResourceKvp(string key,string json) => Kvp[key]=json;
    private static void Log(string text) {}
    private static void Check(bool test,string message) { if (!test) throw new Exception(message); }
    public static void Main() {
        Kvp["veh_old"] = "{\"model\":123,\"plateText\":\"SAME\",\"colors\":{\"primary\":1}}";
        var first = GetSavedVehicleInfo("veh_old");
        Check(first.cadSaveToken.Length==32,"legacy KVP must gain token");
        Check(GetSavedVehicleInfo("veh_old").cadSaveToken==first.cadSaveToken,"legacy upgrade must be idempotent");
        Check(SaveVehicleInfo("veh_renamed",first,false),"rename copy saves");
        Kvp.Remove("veh_old");
        var renamed = GetSavedVehicleInfo("veh_renamed");
        Check(renamed.cadSaveToken==first.cadSaveToken,"rename retains token");
        renamed.colors["primary"]=25; renamed.plateText="CHANGED";
        Check(SaveVehicleInfo("veh_renamed",renamed,true),"customization update saves");
        var customized = GetSavedVehicleInfo("veh_renamed");
        Check(customized.cadSaveToken==first.cadSaveToken && customized.colors["primary"]==25,"customization retains token");
        SaveVehicleInfo("veh_separate",new VehicleInfo{model=123,plateText="SAME"},false);
        Check(GetSavedVehicleInfo("veh_separate").cadSaveToken!=first.cadSaveToken,"fresh identical setup gets separate token");
        Check(!SaveVehicleInfo("veh_renamed",new VehicleInfo(),false),"non-overwrite save must preserve existing data");
        Check(GetSavedVehicleInfo("veh_renamed").cadSaveToken==first.cadSaveToken,"failed save retains identity token");
        Console.WriteLine("vMenu saved-token tests passed (legacy upgrade, rename, customization, separate saves, failed overwrite)");
    }
"""+fields+'\n'+save+'\n'+get+'\n}'
with tempfile.TemporaryDirectory(prefix='psrp-vmenu-token-tests-') as temp:
    work=Path(temp).resolve()
    # Verify the disposable build remains inside the explicitly selected temp parent.
    work.relative_to(Path(tempfile.gettempdir()).resolve())
    reference=escape(str(root/'build/vMenu/Newtonsoft.Json.dll'))
    (work/'Tests.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>'+reference+'</HintPath></Reference></ItemGroup></Project>',encoding='utf-8')
    (work/'Program.cs').write_text(program,encoding='utf-8')
    subprocess.run(['dotnet','run','--project',str(work/'Tests.csproj'),'--configuration','Release','--verbosity','quiet'],check=True)
