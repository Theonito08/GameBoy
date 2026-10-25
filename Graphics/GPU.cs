using GameBoyEmu.Graphics;
using GameBoyEmu.Main;
using GameBoyEmu.Memory;
using System.IO;
namespace GameBoyEmu.Visual
{
    public class GPU : ISaveState
    {
        private MEMORY memory;
        private PPURenderer renderer;
        
        public int scanlineCycles = 0;
        private int currentLine = 0;

        private bool scanlineRendered = false;
        private bool enableSTATInterrupts = true;

        private bool enableHBlankSTAT = true;
        private bool enableVBlankSTAT = true;
        private bool enableOAMSTAT = true;
        private bool enableLYCSTAT = true;
        public bool readyFrame { get; set; }
        public GPU(MEMORY memoryIn, PPURenderer rendererIn)
        {
            memory = memoryIn;
            renderer = rendererIn;
        }

        public void Step(int cycles)
        {
            byte lcdc = memory.ReadByteFromAddress(MEMORY.LCDC);
            bool LCDEnabled = (lcdc & 0x80) != 0;
            if (!LCDEnabled)
            {
                scanlineCycles = 0;
                currentLine = 0;
                renderer.ResetWindowLine();
                scanlineRendered = false;
                memory.ForceWriteByte(MEMORY.LY, 0);
                SetLCDMode(0);
                UpdateLYC();
                return;
            }
            scanlineCycles += cycles;
            byte ly = memory.RawReadByte(MEMORY.LY);
            if (ly < 144 && scanlineCycles >= 252 && !scanlineRendered)
            {
                renderer.RenderScanLine(ly);
                scanlineRendered = true;
            }
            if (ly >= 144)
            {
                SetLCDMode(1); // VBlank region
            }
            else if (scanlineCycles < 80)
            {
                SetLCDMode(2); // OAM scan region
            }
            else if (scanlineCycles < 252)
            {
                SetLCDMode(3); // Pixel Transfer region
            }
            else
            {
                SetLCDMode(0); // HBlank region
            }
            while (scanlineCycles >= 456)
            {
                ly = memory.RawReadByte(MEMORY.LY);
                scanlineCycles -= 456;
                ly++;
                scanlineRendered = false;
                if (ly == 144)
                {
                    SetLCDMode(1);
                    memory.RequestInterruptCall(0);
                    readyFrame = true;
                }

                if (ly >= 154)
                {
                    ly = 0;
                    renderer.ResetWindowLine();
                    SetLCDMode(2);
                }
                currentLine = ly;
                memory.ForceWriteByte(MEMORY.LY, (byte)ly);
                UpdateLYC();
                if (ly < 144 && scanlineCycles >= 252 && !scanlineRendered)
                {
                    renderer.RenderScanLine(ly);
                    scanlineRendered = true;
                }
            }
        }
        
        private void SetLCDMode(byte mode)
        {
            byte stat = memory.RawReadByte(MEMORY.STAT);
            byte oldMode = (byte)(stat & 0x03);
            if (oldMode == mode) return;
            stat = (byte)((stat & 0xFC) | (mode & 0x03));
            memory.ForceWriteByte(MEMORY.STAT, stat);
            if (!enableSTATInterrupts) return;
            bool request = false;

            if (enableHBlankSTAT && mode == 0 && (stat & 0x08) != 0) request = true; // HBlank
            if (enableVBlankSTAT && mode == 1 && (stat & 0x10) != 0) request = true; // VBlank STAT
            if (enableOAMSTAT && mode == 2 && (stat & 0x20) != 0) request = true; // OAM

            if (request) memory.RequestInterruptCall(1);

        }

        private void UpdateLYC()
        {
            byte ly = memory.RawReadByte(MEMORY.LY);
            byte lyc = memory.RawReadByte(MEMORY.LYC);
            byte stat = memory.RawReadByte(MEMORY.STAT);

            bool oldCoincidence = (stat & 0x04) != 0;
            bool newCoincidence = ly == lyc;

            if (newCoincidence) stat |= 0x04;
            else stat &= 0xFB;

            memory.ForceWriteByte(MEMORY.STAT, stat);

            if (enableSTATInterrupts && enableLYCSTAT && newCoincidence && !oldCoincidence && (stat & 0x40) != 0)
            {
                memory.RequestInterruptCall(1);
            }
        }
        public void Save(BinaryWriter stateWriter)
        {
            stateWriter.Write(scanlineCycles);
            stateWriter.Write(currentLine);
            stateWriter.Write(scanlineRendered);
            renderer.Save(stateWriter);
        }
        public void Load(BinaryReader stateReader)
        {
            scanlineCycles = stateReader.ReadInt32();
            currentLine = stateReader.ReadInt32();
            scanlineRendered = stateReader.ReadBoolean();
            renderer.Load(stateReader);
            memory.ForceWriteByte(MEMORY.LY, (byte)currentLine);
        }
        public string GetLCDC()
        {
            return memory.RawReadByte(MEMORY.LCDC).ToString();
        }
        public string GetSTAT()
        {
            return memory.RawReadByte(MEMORY.STAT).ToString();
        }
        public string GetLY()
        {
            return memory.RawReadByte(MEMORY.LY).ToString();
        }
        public string GetLYC()
        {
            return memory.RawReadByte(MEMORY.LYC).ToString("X2");
        }
        public string GetSCX()
        {
            return memory.RawReadByte(MEMORY.SCX).ToString("X2");
        }
        public string GetSCY()
        {
            return memory.RawReadByte(MEMORY.SCY).ToString("X2");
        }
        public string GetWX()
        {
            return memory.RawReadByte(MEMORY.WX).ToString("X2");
        }
        public string GetWY()
        {
            return memory.RawReadByte(MEMORY.WY).ToString("X2");
        }
        public string GetBGP()
        {
            return memory.RawReadByte(MEMORY.BGP).ToString("X2");
        }
        public string GetOBP0()
        {
            return memory.RawReadByte(MEMORY.OBP0).ToString("X2");
        }
        public string GetOBP1()
        {
            return memory.RawReadByte(MEMORY.OBP1).ToString("X2");
        }
        public bool GetLCDEnabled()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x80) != 0;
        }
        public bool GetWindowEnabled()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x20) != 0;
        }
        public bool GetSpritesEnabled()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x02) != 0;
        }
        public bool GetBGEnabled()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x01) != 0;
        }
        public string GetSpriteSize()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x04) != 0 ? "8x16" : "8x8";
        }
        public string GetBGTileMap()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x08) != 0 ? "9C00-9FFF" : "9800-9BFF";
        }
        public string GetTileData()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x10) != 0 ? "8000-8FFF" : "8800-97FF";
        }
        public string GetWindowTileMap()
        {
            byte LCDC = memory.RawReadByte(MEMORY.LCDC);
            return (LCDC & 0x40) != 0 ? "9C00-9FFF" : "9800-9BFF";
        }
        public string GetScanLineMode()
        {
            byte STAT = memory.RawReadByte(MEMORY.STAT);
            switch(STAT & 0x03)
            {
                case 0:
                    return "HBlank";
                case 1:
                    return "VBlank";
                case 2:
                    return "OAM Search";
                case 3:
                    return "Drawing";
                default:
                    return "N/A";
            }
        }
        public string GetCurrentScanLine()
        {
            return memory.RawReadByte(MEMORY.LY).ToString();
        }
        public string GetCyclesIntoLine()
        {
            return scanlineCycles.ToString();
        }

        public void ClearReadyFrame()
        {
            readyFrame = false;
        }
    }
}
