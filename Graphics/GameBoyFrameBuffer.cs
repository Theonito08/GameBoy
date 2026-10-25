using GameBoyEmu.Main;
using System.IO;

namespace GameBoyEmu.Graphics
{
    public class GameBoyFrameBuffer : ISaveState
    {
        private readonly byte[,] pixels = new byte[144, 160];
        public void SetPixel(int x, int y, byte colour)
        {
            if (x < 0 || y < 0 || x >= 160 || y >= 144) return;
            pixels[y, x] = colour;
        }
        public byte GetPixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= 160 || y >= 144) return 0;
            return pixels[y, x];
        }
        public void ClearBuffer()
        {
            for(int y = 0; y < 144; y++)
            {
                for(int x = 0; x < 160; x++)
                {
                    pixels[y, x] = 0;
                }
            }
        }
        public void Save(BinaryWriter stateWriter)
        {
            for (int y = 0; y < 144; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    stateWriter.Write(pixels[y, x]);
                }
            }
        }
        public void Load(BinaryReader stateReader)
        {
            for (int y = 0; y < 144; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    pixels[y, x] = stateReader.ReadByte();
                }
            }
        }
    }
}
