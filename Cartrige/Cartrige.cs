using GameBoyEmu.Debugger;
using GameBoyEmu.Main;
using System;
using System.IO;
using System.Text;

namespace GameBoyEmu.Cartrige
{
    public class Cartridge : ISaveState
    {
        private byte[] externalRAM;
        private byte[] rom;
        private int romBankCount;
        private int ramBankCount;
        private int cartridgeType;
        private IMBC mbc;
        public StringBuilder romName = new StringBuilder();
        public void LoadROMFile(string filepath)
        {
            romName.Clear();
            rom = File.ReadAllBytes(filepath);
            cartridgeType = rom[0x0147];
            romBankCount = GetROMBankCount();
            ramBankCount = GetRAMBankCount();
            externalRAM = new byte[GetRAMSize()];
            switch (cartridgeType)
            {
                //NO MBC
                case 0x00:
                    mbc = new NoMBC(rom, externalRAM);
                    break;
                //MBC1
                case 0x01:
                case 0x02:
                case 0x03:
                    mbc = new MBC1(rom, externalRAM, romBankCount);
                    break;
                //MBC5
                case 0x19:
                case 0x1A:
                case 0x1B:
                case 0x1C:
                case 0x1D:
                case 0x1E:
                    mbc = new MBC5(rom, externalRAM);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Unsupported cartridge type: {cartridgeType:X2}");
            }
            for (ushort i = 0x0134; i < 0x0143; i++)
            {
                if (rom[i] == 0x00)
                {
                    break;
                }

                romName.Append((char)rom[i]);
            }
            if (romName.Length == 0)
            {
                romName.Append("noname");
            }
            Logger.Write(romName.ToString());
            Logger.Write($"Cart type: {cartridgeType}");
            Logger.Write($"ROM size:  {rom[0x0148]:X2}");
            Logger.Write($"ROM banks: {romBankCount}");

        }
        public byte ReadByte(ushort address)
        {
            if (rom == null || mbc == null) return 0xFF;
            return mbc.Read(address);
        }
        public void WriteByte(ushort address, byte value)
        {
            if (mbc == null) return;
            mbc.Write(address, value);
        }
        private int GetROMBankCount()
        {
            switch (rom[0x0148])
            {
                case 0x00:
                    return 2;
                case 0x01:
                    return 4;
                case 0x02:
                    return 8;
                case 0x03:
                    return 16;
                case 0x04:
                    return 32;
                case 0x05:
                    return 64;
                case 0x06:
                    return 128;
                case 0x07:
                    return 256;
                case 0x08:
                    return 512;
                default:
                    return rom.Length / 0x4000;
            }
        }

        private int GetRAMBankCount()
        {
            switch (rom[0x0149])
            {
                case 0x00:
                    return 0;
                case 0x01:
                case 0x02:
                    return 1;
                case 0x03:
                    return 4;
                case 0x04:
                    return 16;
                case 0x05:
                    return 8;
                default:
                    return 0;
            }
        }

        private int GetRAMSize()
        {
            switch (rom[0x0149])
            {
                case 0x00:
                    return 0;
                case 0x01:
                    return 0x800;
                case 0x02:
                    return 0x2000;
                case 0x03:
                    return 0x8000;
                case 0x04:
                    return 0x20000;
                case 0x05:
                    return 0x10000;
                default:
                    return 0;
            }
        }
        public string GetROMName()
        {
            return romName.ToString();
        }
        public string GetROMType()
        {
            byte value = rom[0x0147];
            switch (value)
            {
                case 0x00:
                    return "No MBC";  
                case 0x01:
                    return "MBC1";
                case 0x02:
                    return "MBC1 | RAM";
                case 0x03:
                    return "MBC1 | RAM | BATTERY";
                case 0X05:
                    return "MBC2";
                case 0x06:
                    return "MBC2 | BATTERY";
                case 0x08:
                    return "NO MBC | RAM";
                case 0x09:
                    return "NO MBC | RAM | BATTERY";
                case 0x0B:
                    return "MMM01";
                case 0x0C:
                    return "MMM01 | RAM";
                case 0x0D:
                    return "MMM01 | RAM | BATTERY";
                case 0x0F:
                    return "MBC3 | TIMER | BATTERY";
                case 0x10:
                    return "MBC3 | TIMER | RAM | BATTERY";
                case 0x11:
                    return "MBC3";
                case 0x12:
                    return "MBC3 | RAM";
                case 0x13:
                    return "MBC3 | RAM| BATTERY";
                case 0x19:
                    return "MBC5";
                case 0x1A:
                    return "MBC5 | RAM";
                case 0x1B:
                    return "MBC5 | RAM | BATTERY";
                case 0x1C:
                    return "MBC5 | RUMBLE";
                case 0x1D:
                    return "MBC5 | RUMBLE | RAM";
                case 0x1E:
                    return "MBC5 | RUMBLE | RAM | BATTERY";
                case 0x20:
                    return "MBC6";
                case 0x22:
                    return "MBC7 | SENSOR | RUMBLE";
                case 0xFC:
                    return "camera"; // no use to this and prolly mbc 6 and 7 but a nice touch to include them all i guess.
                default:
                    return null;
            }

            
        }
        public void Save(BinaryWriter stateWriter)
        {
            stateWriter.Write(cartridgeType);
            stateWriter.Write(romBankCount);
            stateWriter.Write(ramBankCount);
            stateWriter.Write(externalRAM?.Length ?? 0);
            if (externalRAM != null) stateWriter.Write(externalRAM);
            mbc.Save(stateWriter);
        }

        public void Load(BinaryReader stateReader)
        {
            cartridgeType = stateReader.ReadInt32();
            romBankCount = stateReader.ReadInt32();
            ramBankCount = stateReader.ReadInt32();
            int ramLength = stateReader.ReadInt32();
            if (ramLength > 0)
            {
                externalRAM = stateReader.ReadBytes(ramLength);
            }
            else
            {
                externalRAM = null;
            }
            mbc.Load(stateReader);
        }
    }
}
