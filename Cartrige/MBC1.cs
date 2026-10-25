using System.IO;

namespace GameBoyEmu.Cartrige
{
    public class MBC1 : IMBC
    {
        private byte[] ROM;
        private byte[] externalRAM;
        private int romBankCount;
        private int romBankLow5 = 1;
        private int bankHigh2 = 0;
        private int bankingMode = 0;
        private bool ramEnabled = false;

        public MBC1(byte[] ROM, byte[] externalRAM, int romBankCount)
        {
            this.ROM = ROM;
            this.externalRAM = externalRAM;
            this.romBankCount = romBankCount;
        }
        public byte Read(ushort address)
        {
            if (address <= 0x7FFF) return ReadROM(address);
            if (address >= 0xA000 && address <= 0xBFFF) return ReadRAM(address);
            return 0xFF;
        }
        public void Write(ushort address, byte value)
        {
            if (address <= 0x7FFF)
            {
                WriteControl(address, value);
                return;
            }
            if (address >= 0xA000 && address <= 0xBFFF) WriteRAM(address, value);
        }

        public byte ReadROM(ushort address)
        {
            if (address <= 0x3FFF)
            {
                if (address < ROM.Length) return ROM[address];
                return 0xFF;
            }
            if (address <= 0x7FFF)
            {
                int bank = GetROMBank();
                int romAddress = bank * 0x4000 + (address - 0x4000);
                if (romAddress >= 0 && romAddress < ROM.Length) return ROM[romAddress];
                return 0xFF;
            }
            return 0xFF;
        }
        private byte ReadRAM(ushort address)
        {
            if (!ramEnabled || externalRAM == null || externalRAM.Length == 0) return 0xFF;
            int ramBank = 0;
            if (bankingMode == 1) ramBank = bankHigh2;
            int ramAddress = ramBank * 0x2000 + (address - 0xA000);
            if (ramAddress >= 0 && ramAddress < externalRAM.Length) return externalRAM[ramAddress];
            return 0xFF;
        }
        private void WriteControl(ushort address, byte value)
        {
            if (address <= 0x1FFF) EnableRAM(value);
            else if (address <= 0x3FFF) SelectBankLowBits(value);
            else if (address <= 0x5FFF) SelectBankHighBits(value);
            else SelectBankingMode(value);
        }
        private void EnableRAM(byte value)
        {
            ramEnabled = (value & 0x0F) == 0x0A;
        }
        private void SelectBankLowBits(byte value)
        {
            romBankLow5 = value & 0x1F;
            if (romBankLow5 == 0) romBankLow5 = 1;
        }
        private void SelectBankHighBits(byte value)
        {
            bankHigh2 = value & 0x03;
        }
        private void SelectBankingMode(byte value)
        {
            bankingMode = value & 0x01;
        }
        private void WriteRAM(ushort address, byte value)
        {
            if (!ramEnabled || externalRAM == null || externalRAM.Length == 0) return;
            int ramBank = 0;
            if (bankingMode == 1) ramBank = bankHigh2;
            int ramAddress = ramBank * 0x2000 + (address - 0xA000);
            if (ramAddress >= 0 && ramAddress < externalRAM.Length) externalRAM[ramAddress] = value;
        }
        private int GetROMBank()
        {
            int bank;
            if (bankingMode == 0) bank = romBankLow5 | (bankHigh2 << 5);
            else bank = romBankLow5;
            if ((bank & 0x1F) == 0) bank++;
            bank %= romBankCount;
            if (bank == 0) bank = 1;
            return bank;
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(romBankLow5);
            writer.Write(bankHigh2);
            writer.Write(bankingMode);
            writer.Write(ramEnabled);
        }
        public void Load(BinaryReader reader)
        {
            romBankLow5 = reader.ReadInt32();
            bankHigh2 = reader.ReadInt32();
            bankingMode = reader.ReadInt32();
            ramEnabled = reader.ReadBoolean();
        }
    }
}
