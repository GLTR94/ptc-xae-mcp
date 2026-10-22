"""Quick ADS variable reader — usage: python ads_read.py <symbol_name> [type]"""
import sys
import pyads

AMS_NET_ID = "192.168.250.20.1.1"
PORT = 851


def read_variable(symbol: str, plc_type: str = "INT") -> None:
    type_map = {
        "BOOL": pyads.PLCTYPE_BOOL,
        "INT": pyads.PLCTYPE_INT,
        "UINT": pyads.PLCTYPE_UINT,
        "DINT": pyads.PLCTYPE_DINT,
        "UDINT": pyads.PLCTYPE_UDINT,
        "REAL": pyads.PLCTYPE_REAL,
        "LREAL": pyads.PLCTYPE_LREAL,
        "STRING": pyads.PLCTYPE_STRING,
        "BYTE": pyads.PLCTYPE_BYTE,
        "WORD": pyads.PLCTYPE_WORD,
        "DWORD": pyads.PLCTYPE_DWORD,
    }
    ads_type = type_map.get(plc_type.upper())
    if ads_type is None:
        print(f"Unknown type '{plc_type}'. Valid types: {', '.join(type_map)}")
        sys.exit(1)

    with pyads.Connection(AMS_NET_ID, PORT) as plc:
        value = plc.read_by_name(symbol, ads_type)
        print(f"{symbol} = {value}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python ads_read.py <symbol_name> [PLC_TYPE]")
        print("  symbol_name  e.g. GVL.nCounter")
        print("  PLC_TYPE     BOOL INT UINT DINT UDINT REAL LREAL STRING BYTE WORD DWORD (default: INT)")
        sys.exit(1)

    symbol_name = sys.argv[1]
    plc_type = sys.argv[2] if len(sys.argv) > 2 else "INT"
    read_variable(symbol_name, plc_type)
