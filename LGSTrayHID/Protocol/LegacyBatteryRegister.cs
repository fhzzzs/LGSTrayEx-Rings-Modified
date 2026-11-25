using LGSTrayHID.Features;
using LGSTrayPrimitives;
namespace LGSTrayHID.Protocol;

// HID++ 1.0 battery charge/mileage (0x0D) is a read-only register.
// 0x07 is deliberately not converted into an exact percent.
public static class LegacyBatteryRegister{
    public static byte[] Request(byte slot)=>[0x10,slot,0x81,0x0D,0,0,0];
    public static (string Model,int Type)? Announcement(byte[] frame){
        if(frame.Length<7||frame[0]!=0x10||frame[2]!=0x41||frame[1] is <1 or >6||(frame[4]&0x40)!=0)return null;
        int kind=frame[4]&15;var type=kind switch{1=>(int)DeviceType.Keyboard,2=>(int)DeviceType.Mouse,_=>-1};
        int wpid=frame[5]|(frame[6]<<8);
        if(type<0||wpid==0||wpid==65535)return null;
        return ($"{(kind==1?"Keyboard":"Mouse")} (WPID {wpid:X4})",type);
    }
    public static BatteryUpdateReturn? DecodeMileage(byte slot,byte[] frame){
        if(frame.Length<7||frame[0]!=0x10||frame[1]!=slot||frame[2]!=0x81||frame[3]!=0x0D||frame[4]>100)return null;
        // Capacity in this register is documented reliable only when
        // discharging or full; charging/error responses are not fabricated.
        var state=frame[6]&0xF0;
        if(state==0x30)return new(frame[4],PowerSupplyStatus.DISCHARGING,0);
        if(state==0x90)return new(frame[4],PowerSupplyStatus.FULL,0);
        return null;
    }
}
