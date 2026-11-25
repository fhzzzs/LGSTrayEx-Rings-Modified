using System;
namespace LGSTrayUI;

// Passive input-report facts from Linux hid-playstation and hid-nintendo.
// Decoders never send output reports or change controller modes.
internal static class ControllerInputBattery
{
    internal static bool Supports(ushort vid,ushort pid)=>vid switch{
        0x3537=>ControllerIdentity.IsCyclone2(vid,pid)||ControllerIdentity.IsG7(vid,pid),
        0x054C=>pid is 0x0CE6 or 0x0DF2 or 0x05C4 or 0x09CC or 0x05C5,
        0x2DC8=>pid is 0x6000 or 0x6100 or 0x6001 or 0x6101 or 0x6003 or 0x6006 or 0x6009,
        0x057E=>pid is 0x2006 or 0x2007 or 0x2009,
        _=>false
    };
    private static bool ValidBluetoothCrc(byte[] data,int count){
        uint crc=0xFFFFFFFF;
        void Add(byte b){crc^=b;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xEDB88320:crc>>1;}
        Add(0xA1);
        for(int i=0;i<count-4;i++)Add(data[i]);
        uint expected=(uint)data[count-4]|((uint)data[count-3]<<8)|((uint)data[count-2]<<16)|((uint)data[count-1]<<24);
        return ~crc==expected;
    }
    internal static (int Level,bool Charging,bool Full)? Decode(ushort vid,ushort pid,byte[] data,int count,bool? bluetooth=null){
        if(!Supports(vid,pid)||count<=0||count>data.Length)return null;
        if(vid==0x3537){
            // G7 configuration telemetry only if a complete matching frame is already available.
            // 10A9 does not answer configuration requests; no request is sent.
            if(ControllerIdentity.IsG7(vid,pid)){
                if(count<61||data[0]!=0x10||data[3]!=0x3C||data[4]!=0xE0||data[32]>1||data[33]>100)return null;
                return(data[33],data[32]==1,data[33]==100&&data[32]==0);
            }
            // Cyclone 2 vendor state report; never apply to G7 Pro modes.
            if(count<37||data[0]!=0x12||(data[1]|data[2]|data[3]|data[4])==0||data[36]>100||data[35]>2)return null;
            if(data[35]==2&&data[36]!=100)return null;
            return(data[36],data[35]==1,data[35]==2);
        }
        if(vid==0x2DC8){
            // Only existing enhanced reports. Feature report 0x06 would
            // switch controller mode and may break DirectInput; never send it.
            if(bluetooth==null&&pid!=0x6009)return null;
            int reportId=pid==0x6009?4:bluetooth==true?1:4;
            if(count<15||data[0]!=reportId)return null;
            int level=data[14]&0x7F;
            // Basic reports are zero-padded by Windows HIDAPI.
            if(level is <1 or >100)return null;
            return(level,(data[14]&0x80)!=0,false);
        }
        if(vid==0x057E){
            // Only the full 0x30 input report, never simple-HID 0x3F.
            if(data[0]!=0x30||count<49)return null;
            int level=data[2]>>5;if(level>4)return null;
            return(level*25,(data[2]&0x10)!=0,level==4);
        }
        if(pid is 0x0CE6 or 0x0DF2){
            int index=bluetooth!=true&&data[0]==1&&count==64?53:bluetooth!=false&&data[0]==0x31&&count==78&&ValidBluetoothCrc(data,count)?54:-1;
            if(index<0)return null;
            int level=data[index]&15,state=data[index]>>4;
            if(state==2)return(100,false,true);
            if(level>10||state>1)return null;
            return(Math.Min(100,level*10+5),state==1,false);
        }
        int offset=bluetooth!=true&&data[0]==1&&count==64?30:bluetooth!=false&&data[0]==0x11&&count==78&&ValidBluetoothCrc(data,count)?32:-1;
        if(offset<0)return null;
        int band=data[offset]&15;bool charging=(data[offset]&16)!=0;
        if(charging&&band==11)return(100,false,true);
        if(band>10)return null;
        return(Math.Min(100,band*10+5),charging,false);
    }
}
