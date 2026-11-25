using System;
using System.Collections.Generic;
using System.Linq;
using LGSTrayPrimitives;

namespace LGSTrayUI;

// Protocol facts referenced in docs/DEVICE-COMPATIBILITY.md. A catalog entry
// allows only a specific, read-only status command on the specified transport.
internal enum AsusProtocol { DirectMouse, OmniMouse, OmniKeyboard, SpeednovaMouse, StrixGoHeadset, DirectKeyboard, DirectHeadset }
internal sealed record AsusProfile(ushort Pid,string Name,DeviceType Type,AsusProtocol Protocol,int BatteryByte=5,int Scale=1,int InterfaceNumber=0);
internal static class PeripheralCatalog
{
    internal const string Revision="1.0.5";
    private static readonly AsusProfile[] asus=Create();
    private static AsusProfile[] Create(){
        var rows=new List<AsusProfile>();
        void M(string name,int scale,params ushort[] ids){foreach(var id in ids)rows.Add(new(id,name,DeviceType.Mouse,AsusProtocol.DirectMouse,4,scale));}
        void O(string name,params ushort[] ids){foreach(var id in ids)rows.Add(new(id,name,DeviceType.Mouse,AsusProtocol.OmniMouse));}
        void K(string name,ushort id,int battery=6)=>rows.Add(new(id,name,DeviceType.Keyboard,AsusProtocol.OmniKeyboard,battery));
        M("ROG Gladius III Aimpoint",1,0x1A72,0x1A70);
        M("ROG Gladius III EVA 2",1,0x1B0C,0x1B0A);
        M("ROG Gladius III Wireless",1,0x197F,0x197D);
        M("ROG Chakram X",1,0x1A1A,0x1A18);
        M("ROG Harpe Ace Aim Lab Edition",1,0x1A94,0x1A92);
        M("ROG Keris Wireless Aimpoint",1,0x1A68,0x1A66);
        M("ROG Spatha X",1,0x1979,0x1977);
        M("TUF Gaming M4 Wireless",1,0x19F4);
        M("TX Gaming Mouse",1,0x1A8D);
        M("TX Gaming Mouse Mini",1,0x1AF5,0x1AF3);
        M("TX Gaming Mouse Mini Miku",1,0x1C5A);
        M("TUF Gaming Mini Miku Edition",1,0x1C57,0x1C56);
        M("ROG Chakram",25,0x18E5,0x18E3);
        M("ROG Keris Wireless",25,0x1960,0x195E);
        M("ROG Keris EVA Edition",25,0x1A59,0x1A57);
        M("ROG Pugio II",25,0x1908,0x1906);
        M("ROG Strix Impact II Wireless",25,0x1949,0x1947);
        O("ROG Harpe Ace Mini",0x1B65);
        O("ROG Keris II Origin",0x1C0E);
        O("ROG Keris II Origin KJP",0x1D4E);
        O("ROG Harpe Ace Aim Lab Edition",0x1A94);
        O("ROG Strix Impact III Wireless",0x1AD7);
        O("ROG Gladius III Aimpoint",0x1A72);
        O("ROG Keris Wireless Aimpoint",0x1A68,0x1A6A);
        O("ROG Keris II Ace",0x1B1A,0x1B18);
        O("ROG Harpe Ace Extreme",0x1B68,0x1B69);
        K("ROG Azoth",0x1A85);K("ROG Azoth Extreme",0x1B42);K("ROG Azoth Extreme SE",0x1CF1);
        K("ROG Strix Scope II 96 Wireless",0x1AB0);K("ROG Strix Scope II 96 RX Wireless",0x1B7A);
        K("ROG Falchion RX Low Profile",0x1B06,11);
        // Morph's battery layout has not been confirmed; do not enable it.
        rows.Add(new(0x1C69,"ROG Harpe II Ace / Extreme",DeviceType.Mouse,AsusProtocol.SpeednovaMouse,4));
        // Shared 1AD0 SPEEDNOVA receiver: model is not established by PID alone.
        // It is recognized only with a trustworthy product string in the reader.
        rows.Add(new(0x1AD0,"ROG Harpe II Ace / Extreme",DeviceType.Mouse,AsusProtocol.SpeednovaMouse));
        rows.Add(new(0x18D6,"ROG Strix Go 2.4",DeviceType.Headset,AsusProtocol.StrixGoHeadset));
        rows.Add(new(0x18D7,"ROG Strix Go 2.4",DeviceType.Headset,AsusProtocol.StrixGoHeadset));
        void DK(string name,int offset,params ushort[] ids){foreach(var id in ids)rows.Add(new(id,name,DeviceType.Keyboard,AsusProtocol.DirectKeyboard,offset));}
        DK("ROG Azoth",6,0x1A83,0x1A85);
        DK("ROG Azoth Extreme",6,0x1B3F);
        DK("ROG Azoth Extreme SE",6,0x1CEF);
        DK("ROG Azoth X",6,0x1C24,0x1C25);
        DK("ROG Falchion",11,0x193C,0x193E);
        DK("ROG Falchion RX Low Profile",11,0x1B04);
        DK("ROG Claymore II",6,0x196B);
        DK("ROG Strix Scope II 96 Wireless",6,0x1AAE);
        DK("ROG Strix Scope II 96 RX Wireless",6,0x1B78);
        rows.Add(new(0x1AFA,"ROG Delta II",DeviceType.Headset,AsusProtocol.DirectHeadset,6));
        rows.Add(new(0x1D41,"ROG Delta II KJP",DeviceType.Headset,AsusProtocol.DirectHeadset,6));
        rows.Add(new(0x1B84,"ROG Pelta",DeviceType.Headset,AsusProtocol.DirectHeadset,6));
        rows.Add(new(0x1AD3,"ROG Cetra SpeedNova",DeviceType.Headset,AsusProtocol.DirectHeadset,6));
        rows.Add(new(0x1A24,"ASUS Mouse MD200",DeviceType.Mouse,AsusProtocol.DirectMouse,4,1,2));
        rows.Add(new(0x1CD3,"ASUS ProArt Mouse MD301",DeviceType.Mouse,AsusProtocol.DirectMouse,4,1,3));
        rows.Add(new(0x18A0,"ROG Gladius II Wireless",DeviceType.Mouse,AsusProtocol.DirectMouse,4,25,2));
        rows.Add(new(0x18B4,"ROG Strix Carry",DeviceType.Mouse,AsusProtocol.DirectMouse,6,25,1));
        return rows.ToArray();
    }
    internal static IReadOnlyList<AsusProfile> Asus=>asus;
    internal static AsusProfile? Find(ushort pid,AsusProtocol protocol)=>asus.FirstOrDefault(x=>x.Pid==pid&&x.Protocol==protocol);
    internal static DeviceType? NamedType(string name){
        if(PeripheralDisplayCatalog.Classify(name) is { } named)return named;
        string n=name.ToUpperInvariant();
        if(n.Contains("DUALSENSE")||n.Contains("DUALSHOCK")||n.Contains("JOY-CON")||n.Contains("SWITCH PRO CONTROLLER")||n.Contains("8BITDO PRO")||n.Contains("8BITDO ULTIMATE")||n.Contains("8BITDO N30")||n.Contains("8BITDO SN30")||n.Contains("GAMESIR")||n.Contains("CONTROLLER")||n.Contains("GAMEPAD"))return DeviceType.Gamepad;
        if(n.Contains("MX KEYS")||n.Contains("MX MECHANICAL")||n.Contains("AZOTH")||n.Contains("FALCHION")||n.Contains("STRIX SCOPE")||n.Contains("STRIX MORPH")||n.Contains("KEYBOARD"))return DeviceType.Keyboard;
        if(n.Contains("MX MASTER")||n.Contains("MX ANYWHERE")||n.Contains("SUPERLIGHT")||n.Contains("HARPE")||n.Contains("KERIS")||n.Contains("GLADIUS")||n.Contains("CHAKRAM")||n.Contains("PUGIO")||n.Contains("SPATHA")||n.Contains("MOUSE"))return DeviceType.Mouse;
        if(n.Contains("G735")||n.Contains("ROG DELTA")||n.Contains("ROG CETRA")||n.Contains("ROG PELTA")||n.Contains("STRIX GO")||n.Contains("HEADSET")||n.Contains("HEADPHONE")||n.Contains("EARBUD")||n.Contains("AIRPODS"))return DeviceType.Headset;
        return null;
    }
}
