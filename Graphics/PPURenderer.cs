using GameBoyEmu.Main;
using GameBoyEmu.Memory;
using System.IO;

namespace GameBoyEmu.Graphics
{
    public class PPURenderer : ISaveState
    {
        private MEMORY memory;
        private GameBoyFrameBuffer frameBuffer;
        private byte[,] colourID = new byte[144, 160];
        private int windowLine = 0;

        public PPURenderer(MEMORY memoryIn, GameBoyFrameBuffer frameBufferIn)
        {
            memory = memoryIn;
            frameBuffer = frameBufferIn;
        }
        public void RenderScanLine(int lineNum)
        {
            byte lcdc = memory.ReadByteFromAddress(MEMORY.LCDC);

            bool bgEnabled = (lcdc & 0x01) != 0;
            bool windowEnabled = (lcdc & 0x20) != 0;
            bool spritesEnabled = (lcdc & 0x02) != 0;

            if (bgEnabled) RenderBackground(lineNum, lcdc);
            else ClearScanLine(lineNum);
            byte wY = memory.ReadByteFromAddress(MEMORY.WY);
            byte wX = memory.ReadByteFromAddress(MEMORY.WX);
            bool windowVisible = bgEnabled && windowEnabled && lineNum >= wY && wX <= 166;
            if (windowVisible)
            {
                RenderWindow(lineNum, windowLine);
                windowLine++;
            }
            if (spritesEnabled) RenderSprites(lineNum);
        }
        private void RenderBackground(int lineNum, byte lcdc)
        {
            bool highTileMap = (lcdc & 0x08) != 0;
            ushort tileMap = highTileMap ? (ushort)0x9C00 : (ushort)0x9800;
            byte scy = memory.ReadByteFromAddress(MEMORY.SCY);
            byte scx = memory.ReadByteFromAddress(MEMORY.SCX);
            int bgY = (lineNum + scy) & 0xFF;
            int tileRow = bgY / 8;
            int rowInTile = bgY % 8;
            for (int screenX = 0; screenX < 160; screenX++)
            {
                int bgX = (screenX + scx) & 0xFF;
                int tileX = bgX / 8;
                int columnInTile = bgX % 8;
                byte tileIndex = PPURead((ushort)(tileMap + tileRow * 32 + tileX));
                bool unsignedMode = (lcdc & 0x10) != 0;
                ushort tileAddress = GetTileAddress(tileIndex, unsignedMode);
                ushort rowAddress = (ushort)(tileAddress + rowInTile * 2);
                byte low = PPURead(rowAddress);
                byte high = PPURead((ushort)(rowAddress + 1));
                int bit = 7 - columnInTile;
                int lowBit = (low >> bit) & 1;
                int highBit = (high >> bit) & 1;
                int colour = lowBit + highBit * 2;
                colourID[lineNum, screenX] = (byte)colour;
                frameBuffer.SetPixel(screenX, lineNum, GetBackgroundWindowShade(colour));
            }
        }
        private void RenderWindow(int lineNum, int windowLine)
        {
            byte lcdc = memory.ReadByteFromAddress(MEMORY.LCDC);
            byte wX = memory.ReadByteFromAddress(MEMORY.WX);
            int windowX = wX - 7;
            if (windowX >= 160) return;
            int tileRow = windowLine / 8;
            int rowInTile = windowLine % 8;
            bool highWindowTileMap = (lcdc & 0x40) != 0;
            ushort tileMap = highWindowTileMap ? (ushort)0x9C00 : (ushort)0x9800;
            bool unsignedMode = (lcdc & 0x10) != 0;
            for (int screenX = 0; screenX < 160; screenX++)
            {
                int windowPixelX = screenX - windowX;
                if (windowPixelX < 0) continue;
                int tileX = windowPixelX / 8;
                int columnInTile = windowPixelX % 8;
                byte tileIndex = PPURead((ushort)(tileMap + tileRow * 32 + tileX));
                ushort tileAddress = GetTileAddress(tileIndex, unsignedMode);
                ushort rowAddress = (ushort)(tileAddress + rowInTile * 2);
                byte low = PPURead(rowAddress);
                byte high = PPURead((ushort)(rowAddress + 1));
                int bit = 7 - columnInTile;
                int lowBit = (low >> bit) & 1;
                int highBit = (high >> bit) & 1;
                int colour = lowBit + highBit * 2;
                colourID[lineNum, screenX] = (byte)colour;
                frameBuffer.SetPixel(screenX, lineNum, GetBackgroundWindowShade(colour));
            }
        }
        private void RenderSprites(int lineNum)
        {
            byte lcdc = memory.ReadByteFromAddress(MEMORY.LCDC);
            int spriteHeight = (lcdc & 0x04) != 0 ? 16 : 8;
            int[] visibleSprites = new int[10];
            int count = 0;
            for (int sprite = 0; sprite < 40 && count < 10; sprite++)
            {
                ushort oamAddress = (ushort)(0xFE00 + sprite * 4);
                int spriteY = PPURead(oamAddress) - 16;
                if (lineNum >= spriteY && lineNum < spriteY + spriteHeight)
                {
                    visibleSprites[count] = sprite;
                    count++;
                }
            }
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    int spriteI = visibleSprites[i];
                    int spriteJ = visibleSprites[j];
                    ushort addressI = (ushort)(0xFE00 + spriteI * 4);
                    ushort addressJ = (ushort)(0xFE00 + spriteJ * 4);
                    int xI = PPURead((ushort)(addressI + 1));
                    int xJ = PPURead((ushort)(addressJ + 1));
                    bool spriteJIsLowerPriority = (xJ > xI || (xJ == xI && spriteJ > spriteI));
                    if (spriteJIsLowerPriority)
                    {
                        int tempSprite = visibleSprites[i];
                        visibleSprites[i] = visibleSprites[j];
                        visibleSprites[j] = tempSprite;
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                int sprite = visibleSprites[i];
                ushort OAMAddress = (ushort)(0xFE00 + sprite * 4);
                int spriteY = PPURead(OAMAddress) - 16;
                int spriteX = PPURead((ushort)(OAMAddress + 1)) - 8;
                byte tileIndex = PPURead((ushort)(OAMAddress + 2));
                byte attributes = PPURead((ushort)(OAMAddress + 3));
                bool behindBackground = (attributes & 0x80) != 0;
                bool yFlip = (attributes & 0x40) != 0;
                bool xFlip = (attributes & 0x20) != 0;
                int rowInSprite = lineNum - spriteY;
                if (yFlip) rowInSprite = spriteHeight - 1 - rowInSprite;
                int tileToUse = tileIndex;
                int rowInTile = rowInSprite;
                if (spriteHeight == 16)
                {
                    tileToUse = tileIndex & 0xFE;
                    if (rowInSprite >= 8)
                    {
                        tileToUse++;
                        rowInTile = rowInSprite - 8;
                    }
                }
                else rowInTile = rowInSprite;
                ushort tileAddress = (ushort)(0x8000 + tileToUse * 16 + rowInTile * 2);
                byte lowByte = PPURead(tileAddress);
                byte highByte = PPURead((ushort)(tileAddress + 1));
                for (int x = 0; x < 8; x++)
                {
                    int screenX = spriteX + x;
                    if (screenX < 0 || screenX >= 160) continue;
                    int pixelInTile = xFlip ? 7 - x : x;
                    int bit = 7 - pixelInTile;
                    int lowBit = (lowByte >> bit) & 1;
                    int highBit = (highByte >> bit) & 1;
                    int colour = lowBit + highBit * 2;
                    if (colour == 0) continue;
                    if (behindBackground && colourID[lineNum, screenX] != 0) continue;
                    frameBuffer.SetPixel(screenX, lineNum, GetSpriteShade(colour, attributes));
                }
            }

        }
        public void ClearScanLine(int lineNum)
        {
            for (int x = 0; x < 160; x++)
            {
                frameBuffer.SetPixel(x, lineNum, 0);
                colourID[lineNum, x] = 0;
            }
        }
        private byte GetBackgroundWindowShade(int colourId)
        {
            byte bgp = memory.ReadByteFromAddress(MEMORY.BGP);
            int shade = (bgp >> (colourId * 2)) & 0x03;
            return (byte)shade;
        }
        private byte GetSpriteShade(int colour, byte attributes)
        {
            bool useOBP1 = (attributes & 0x10) != 0;
            byte pallete = useOBP1 ? memory.ReadByteFromAddress(MEMORY.OBP1) : memory.ReadByteFromAddress(MEMORY.OBP0);
            int shade = (pallete >> (colour * 2)) & 0x03;
            return (byte)shade;
        }
        private byte PPURead(ushort address)
        {
            return memory.RawReadByte(address);
        }
        public void ResetWindowLine()
        {
            windowLine = 0;
        }
        private ushort GetTileAddress(byte tileIndex, bool unsignedMode)
        {
            if (unsignedMode) return (ushort)(0x8000 + tileIndex * 16);
            return (ushort)(0x9000 + (sbyte)tileIndex * 16);
        }
        public byte GetPixel(int x, int y)
        {
            return frameBuffer.GetPixel(x, y);
        }
        public byte GetColourID(int x, int y)
        {
            return colourID[y, x];
        }
        public void Save(BinaryWriter stateWriter)
        {
            stateWriter.Write(windowLine);
            for (int y = 0; y < 144; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    stateWriter.Write(colourID[y, x]);
                }
            }
            frameBuffer.Save(stateWriter);
        }
        public void Load(BinaryReader stateReader)
        {
            windowLine = stateReader.ReadInt32();
            for (int y = 0; y < 144; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    colourID[y, x] = stateReader.ReadByte();
                }
            }
            frameBuffer.Load(stateReader);
        }
    }
}
