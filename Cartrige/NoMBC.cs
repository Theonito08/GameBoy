using System.IO;
namespace GameBoyEmu.Cartrige
{
    public class NoMBC : IMBC
    {
        private byte[] ROM;
        private byte[] externalRAM;
        
        public NoMBC(byte[] ROM, byte[] externalRAM)
        {
            this.ROM = ROM;
            this.externalRAM = externalRAM;
        }
        public byte Read(ushort address)
        {
            if (address <= 0x7FFF) return ReadROM(address);
            if (address >= 0xA000 && address <= 0xBFFF) return ReadRAM(address);
            return 0xFF;
        }
        public void Write(ushort address, byte value)
        {
            if (address >= 0xA000 && address <= 0xBFFF) WriteRAM(address, value);
        }
        private byte ReadROM(ushort address)
        {
            if (address < ROM.Length) return ROM[address];
            return 0xFF;
        }
        private byte ReadRAM(ushort address)
        {
            if (externalRAM == null || externalRAM.Length == 0) return 0xFF;
            int ramAddress = address - 0xA000;
            if (ramAddress < externalRAM.Length) return externalRAM[ramAddress];
            return 0xFF;
        }
        private void WriteRAM(ushort address, byte value)
        {
            if (externalRAM == null || externalRAM.Length == 0) return;
            int ramAddress = address - 0xA000;
            if (ramAddress < externalRAM.Length) externalRAM[ramAddress] = value;
        }

        public void Save(BinaryWriter writer)
        {
            // well its no mbc sooooo....
        }
        public void Load(BinaryReader reader)
        {
            // if i dont save anything, aint gotta load anything either.
        }
    }
}
