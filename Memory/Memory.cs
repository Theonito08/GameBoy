using GameBoyEmu.Audio;
using GameBoyEmu.Cartrige;
using GameBoyEmu.Input;
using GameBoyEmu.Main;
using System;
using System.IO;

namespace GameBoyEmu.Memory
{
    /// <summary>
    /// Represents the Game Boy memory bus.
    /// This class maps CPU reads and writes to the correct hardware area:
    /// cartridge ROM/RAM, work RAM, echo RAM, OAM, IO registers, timers,
    /// joypad input, sound registers, and interrupt registers.
    /// </summary>

    public class MEMORY : ISaveState
    {
        private IJoypad joypad;
        // 64KB of memory on the original DMG.
        private byte[] memory = new byte[65536];

        // Connecting the hardware here:
        private Cartridge ROMCartrige;
        private APU apu;

        // GPU / PPU registers
        public const ushort LCDC = 0xFF40;
        public const ushort SCY = 0xFF42;
        public const ushort SCX = 0xFF43;
        public const ushort LY = 0xFF44;
        public const ushort STAT = 0xFF41;
        public const ushort LYC = 0xFF45;
        public const ushort BGP = 0xFF47;
        public const ushort OBP0 = 0xFF48;
        public const ushort OBP1 = 0xFF49;
        public const ushort WY = 0xFF4A;
        public const ushort WX = 0xFF4B;
        public const ushort DMA = 0xFF46;

        // CONTROL OAM DMA State
        // Writing to FF46 starts the 160 byte transfer into 0xFE00-0xFE9F
        // This is used in order to time it likeit would in real hardware, as the transfer wasnt instant.
        // Happens of multiple cpu cycles so it needs to be tracked.
        public bool OAMDMAActive = false;
        private ushort OAMDMASource = 0;
        private int OAMDMAIndex = 0;
        private int OAMDMACycleCounter = 0;
        private int OAMDMAStartDelay = 0;

        // Input and interrupt registers
        public const ushort JOYPAD = 0xFF00;
        public const ushort IE = 0xFFFF;
        public const ushort IF = 0xFF0F;




        // Timer registers
        public const ushort DIV = 0xFF04;
        public const ushort TIMA = 0xFF05;
        public const ushort TMA = 0xFF06;
        public const ushort TAC = 0xFF07;
        private bool timerReloading = false;
        private int timerReloadingCyclesLeft = 0;



        // MAINLY FOR BLARGG TEST ROMS (serial registers)
        public const ushort SB = 0xFF01;
        public const ushort SC = 0xFF02;

        /// <summary>
        /// Approximations i found online for register values after boot rom,
        /// since i can't use a boot rom because thats illegal.
        /// </summary>
        public MEMORY()
        {
            memory[LCDC] = 0x91;
            memory[SCY] = 0x00;
            memory[SCX] = 0x00;
            memory[LY] = 0x00;
            memory[BGP] = 0xE4;
            memory[OBP0] = 0xE4;
            memory[OBP1] = 0x1B;
            memory[JOYPAD] = 0xFF;
            memory[IF] = 0x01;
            memory[WY] = 0x00;
            memory[WX] = 0x00;
            memory[STAT] = 0x85;
            memory[LYC] = 0x00;
        }
        public void MountCartrige(Cartridge CartIn)
        {
            ROMCartrige = CartIn;
        }
        public void ConnectJoypad(IJoypad input)
        {
            joypad = input;
        }

        public void ConnectAPU(APU APUIn)
        {
            apu = APUIn;
        }

        /// <summary>
        /// Reads one byte from the memory map.
        /// Some addresses are not simple RAM reads: cartridge areas, joypad,
        /// IF, STAT, echo RAM, unusable OAM, and DMA behaviour all need special handling.
        /// Implementations differ.
        /// </summary>
        public byte ReadByteFromAddress(ushort address)
        {
            if (address == IF)
            {
                // Upper three bits of IF always read as 1 on Game Boy.
                // Internally only bits 0-4 are stored.
                return (byte)(memory[IF] | 0xE0);
            }

            if (address == JOYPAD)
            {
                // Reads from this addresses are passed through the joypad function.
                byte value = ReadInputFromJoypad();
                return value;
            }

            if (address <= 0x7FFF && ROMCartrige != null)
            {
                // Cartridge ROM area. Reads are forwarded to the cartridge so MBC
                // bank switching can be handled there.
                return ROMCartrige.ReadByte(address);
            }

            if (address >= 0xA000 && address <= 0xBFFF && ROMCartrige != null)
            {

                return ROMCartrige.ReadByte(address);
            }
            if (address >= 0xE000 && address <= 0xFDFF)
            {
                // Echo RAM mirrors [0xE000 - 0x2000]-[0xFDFF - 0x2000].
                // Which is 0xC000-0xDDFF
                return memory[address - 0x2000];
            }
            if (OAMDMAActive && OAMDMAStartDelay == 0 && address >= 0xFE00 && address <= 0xFE9F)
            {
                // During active OAM DMA, CPU reads from OAM are blocked.
                return 0xFF;
            }
            if (address >= 0xFEA0 && address <= 0xFEFF)
            {
                // FEA0-FEFF is unusable memory for some reason, dk.
                return 0xFF;
            }
            if (address == STAT)
            {
                return (byte)(memory[STAT] | 0x80);
            }
            if (address >= 0xFE00 && address <= 0xFE9F)
            {
                /*Console.WriteLine(
                    $"OAM READ addr={address:X4} active={OAMDMAActive} delay={OAMDMAStartDelay} index={OAMDMAIndex}"
                );*/
            }
            if (address == DMA)
            {
                return memory[DMA];
            }
            /*bool lcdEnabled = (memory[LCDC] & 0x80) != 0;
            byte lcdMode = (byte)(memory[STAT] & 0x03);
            if (lcdEnabled && address >= 0x8000 && address <= 0x9FFF && lcdMode == 3)
            {
                // CPU cannot read VRAM during pixel transfer.
                return 0xFF;
            }
            if (lcdEnabled && address >= 0xFE00 && address <= 0xFE9F && (lcdMode == 2 || lcdMode == 3 || (OAMDMAActive && OAMDMAStartDelay == 0)))
            {
                // CPU cannot read OAM during OAM scan, pixel transfer, or active OAM DMA.
                return 0xFF;
            }*/
            if (address >= 0xFF10 && address <= 0xFF3F)
            {
                if (apu != null)
                {
                    return apu.ReadFromSoundRegister(address);
                }

                return memory[address];
            }
            return memory[address];
        }
        public byte RawReadByte(ushort address)
        {
            return memory[address];
        }

        /// <summary>
        /// Writes one byte to the memory map.
        /// This handles memory-mapped hardware behaviour such as cartridge banking,
        /// DMA start, timer register side effects, echo RAM, LCD registers, and sound IO.
        /// </summary>
        public void WriteByteToAddress(ushort address, byte value)
        {

            if (address <= 0x7FFF && ROMCartrige != null)
            {
                ROMCartrige.WriteByte(address, value);
                return;
            }
            if (address >= 0xA000 && address <= 0xBFFF && ROMCartrige != null)
            {
                ROMCartrige.WriteByte(address, value);
                return;
            }
            if (address == JOYPAD)
            {
                // Only bits 4 and 5 are writable. They determine button or direction input.
                memory[JOYPAD] = (byte)((memory[JOYPAD] & 0xCF) | (value & 0x30));
                return;
            }
            if (address == LY)
            {
                // Writing to LY resets it to 0.
                memory[LY] = 0;
                return;
            }
            if (address == SC)
            {
                // Blargg writes 0x81 to SC when it wants to send one serial character
                memory[SC] = value;

                if (value == 0x81)
                {
                    byte b = memory[SB];

                    if (b >= 32 && b <= 126)
                        Console.Write((char)b);
                    else
                        Console.Write($"<{b:X2}>"); // shows invisible bytes

                    memory[SC] = 0x01;
                }

                return;
            }
            if (address == SB)
            {
                memory[address] = value;
                return;
            }
            if (address == DIV)
            {
                // Writing to DIV resets the internal divider.
                // This can also trigger a TIMA increment if it causes a falling timer edge.
                bool oldSignal = GetTimerSignal();

                divCounter = 0;
                memory[DIV] = 0;

                bool newSignal = GetTimerSignal();

                if (oldSignal && !newSignal)
                {
                    IncrementTIMA();
                }

                return;
            }
            if (address == IF)
            {
                // Only interrupt bits 0-4 are stored.
                // Bits 5-7 read back as 1 in ReadByteFromAddress.
                memory[IF] = (byte)(value & 0x1F);
                return;
            }

            if (address == TIMA)
            {
                // TIMA is ignored during reload cycle.
                if (timerReloading)
                {
                    return;
                }
                memory[TIMA] = value;

                // During the overflow delay, writing TIMA cancels the pending reload.
                timerOverflowPending = false;
                timerOverflowDelay = 0;
                return;
            }

            if (address == TMA)
            {
                memory[TMA] = value;
                // If TMA is written during the reload cycle, TIMA also receives the new value.
                if (timerReloading)
                {
                    memory[TIMA] = value;
                }

                return;
            }

            if (address == TAC)
            {
                bool oldSignal = GetTimerSignal();
                memory[TAC] = (byte)(value & 0x07);
                bool newSignal = GetTimerSignal();
                if (oldSignal && !newSignal)
                {
                    IncrementTIMA();
                }
                return;
            }
            if (address >= 0xFF10 && address <= 0xFF3F)
            {
                apu?.WriteToSoundRegister(address, value);
                return;
            }
            if (address >= 0xE000 && address <= 0xFDFF)
            {
                //Echo RAM mirroring again.
                memory[address] = value;
                memory[address - 0x2000] = value;
                return;
            }

            if (address >= 0xC000 && address <= 0xDDFF)
            {
                memory[address] = value;
                memory[address + 0x2000] = value;
                return;
            }
            if (address >= 0xFEA0 && address <= 0xFEFF)
            {
                return;
            }
            if (address == STAT)
            {
                // Bits 0-2 are controlled by the GPU.
                // CPU can only write the STAT interrupt enable bits.
                memory[STAT] = (byte)((memory[STAT] & 0x07) | (value & 0x78) | 0x80);
                return;
            }
            if (address == LYC)
            {
                memory[LYC] = value;
                return;
            }
            if (address == LCDC)
            {
                bool wasOn = (memory[LCDC] & 0x80) != 0;
                bool nowOn = (value & 0x80) != 0;

                memory[LCDC] = value;

                if (wasOn && !nowOn)
                {
                    memory[LY] = 0;
                    memory[STAT] = (byte)((memory[STAT] & 0xFC) | 0x00 | 0x80);
                }

                return;
            }
            if (address == DMA)
            {
                memory[DMA] = value;
                OAMDMAActive = true;
                OAMDMASource = (ushort)(value << 8);
                OAMDMAIndex = 0;
                OAMDMACycleCounter = 0;
                OAMDMAStartDelay = 8;
                return;
            }
            /* bool lcdEnabled = (memory[LCDC] & 0x80) != 0;
             byte lcdMode = (byte)(memory[STAT] & 0x03);
             if (lcdEnabled && address >= 0x8000 && address <= 0x9FFF && lcdMode == 3)
             {
                 // CPU cannot write VRAM during pixel transfer.
                 return;
             }

             if (lcdEnabled && address >= 0xFE00 && address <= 0xFE9F &&
                 (lcdMode == 2 || lcdMode == 3 || (OAMDMAActive && OAMDMAStartDelay == 0)))
             {
                 // CPU cannot write OAM during OAM scan, pixel transfer, or active OAM DMA.
                 return;
             }*/
            if (OAMDMAActive && OAMDMAStartDelay == 0 && address >= 0xFE00 && address <= 0xFE9F)
            {
                return;
            }
            memory[address] = value;
        }

        public void TriggerOAMBugIDU(ushort address, int scanlineCycles)
        {
            // IDU bug only matters when the address bus shows FE00-FEFF
            if (address < 0xFE00 || address > 0xFEFF)
                return;

            if (!OAMBugCanHappen())
                return;

            int row = GetOAMBugRow(scanlineCycles);

            if (row <= 0 || row >= 20)
                return;

            ApplyOAMBugWrite(row);
        }

        public void TriggerOAMBugAccess(ushort address, int scanlineCycles)
        {
            // Temporary: make your existing CPU code compile.
            // Later, split this into read/write properly.
            TriggerOAMBugRead(address, scanlineCycles);
        }

        public void TriggerOAMBugRead(ushort address, int scanlineCycles)
        {
            if (address < 0xFE00 || address > 0xFEFF)
                return;

            if (!OAMBugCanHappen())
                return;

            int row = GetOAMBugRow(scanlineCycles);

            if (row <= 0 || row >= 20)
                return;

            ApplyOAMBugRead(row);
        }
        public void TriggerOAMBugReadIDU(ushort address, int scanlineCycles)
        {
            if (address < 0xFE00 || address > 0xFEFF)
                return;

            if (!OAMBugCanHappen())
                return;

            int row = GetOAMBugRow(scanlineCycles);

            if (row <= 0 || row >= 20)
                return;

            ApplyOAMBugReadIDU(row);
        }

        public void TriggerOAMBugWrite(ushort address, int scanlineCycles)
        {
            if (address < 0xFE00 || address > 0xFEFF)
                return;

            if (!OAMBugCanHappen())
                return;

            int row = GetOAMBugRow(scanlineCycles);

            if (row <= 0 || row >= 20)
                return;

            ApplyOAMBugWrite(row);
        }

        private bool OAMBugCanHappen()
        {
            byte lcdc = RawReadByte(LCDC);

            // LCD off = no PPU OAM scan
            if ((lcdc & 0x80) == 0)
                return false;

            int mode = RawReadByte(STAT) & 0x03;

            // OAM corruption bug happens during mode 2
            return mode == 2;
        }

        private int GetOAMBugRow(int scanlineCycles)
        {
            // Mode 2 is 80 cycles = 20 M-cycles.
            // Each OAM row is 8 bytes, so row advances every 4 CPU cycles here.
            int row = (scanlineCycles) / 4;

            if (row < 0) row = 0;
            if (row > 19) row = 19;

            return row;
        }

        private ushort ReadOAMWord(int row, int word)
        {
            int offset = row * 8 + word * 2;
            ushort address = (ushort)(0xFE00 + offset);
            byte low = RawReadByte(address);
            byte high = RawReadByte((ushort)(address + 1));
            return (ushort)(low | (high << 8));
        }
        private void WriteOAMWord(int row, int word, ushort value)
        {
            int offset = row * 8 + word * 2;
            ushort address = (ushort)(0xFE00 + offset);
            ForceWriteByte(address, (byte)(value & 0xFF));
            ForceWriteByte((ushort)(address + 1), (byte)(value >> 8));
        }
        private void ApplyOAMBugWrite(int row)
        {
            ushort a = ReadOAMWord(row, 0);
            ushort b = ReadOAMWord(row - 1, 0);
            ushort c = ReadOAMWord(row - 1, 2);
            ushort newWord0 = (ushort)(((a ^ c) & (b ^ c)) ^ c);// FROM PANDOCS
            WriteOAMWord(row, 0, newWord0);
            WriteOAMWord(row, 1, ReadOAMWord(row - 1, 1));
            WriteOAMWord(row, 2, ReadOAMWord(row - 1, 2));
            WriteOAMWord(row, 3, ReadOAMWord(row - 1, 3));
        }

        private void ApplyOAMBugRead(int row)
        {
            ushort a = ReadOAMWord(row, 0);
            ushort b = ReadOAMWord(row - 1, 0);
            ushort c = ReadOAMWord(row - 1, 2);
            ushort newWord0 = (ushort)(b | (a & c));// FROM PANDOCS
            WriteOAMWord(row, 0, newWord0);
            WriteOAMWord(row, 1, ReadOAMWord(row - 1, 1));
            WriteOAMWord(row, 2, ReadOAMWord(row - 1, 2));
            WriteOAMWord(row, 3, ReadOAMWord(row - 1, 3));
        }

        private void CopyOAMRow(int fromRow, int toRow)
        {
            for (int word = 0; word < 4; word++)
            {
                WriteOAMWord(toRow, word, ReadOAMWord(fromRow, word));
            }
        }

        private void ApplyOAMBugReadIDU(int row)
        {
            if (row > 3 && row < 19)
            {
                ushort a = ReadOAMWord(row - 2, 0);
                ushort b = ReadOAMWord(row - 1, 0);
                ushort c = ReadOAMWord(row, 0);
                ushort d = ReadOAMWord(row - 1, 2);

                ushort newWord0 = (ushort)((b & (a | c | d)) | (a & c & d));

                WriteOAMWord(row - 1, 0, newWord0);

                CopyOAMRow(row - 1, row);
                CopyOAMRow(row - 1, row - 2);
            }

            ApplyOAMBugRead(row);
        }
        public ushort ReadWord(ushort address)
        {
            byte lowByte = ReadByteFromAddress(address);
            byte highByte = ReadByteFromAddress((ushort)(address + 1));
            return (ushort)(lowByte | (highByte << 8));
        }

        public void WriteWord(ushort address, ushort value)
        {
            WriteByteToAddress(address, (byte)(value & 0xFF));
            WriteByteToAddress(address, (byte)(value >> 8));
        }

        public void ForceWriteByte(ushort address, byte value)
        {
            memory[address] = value;
        }

        public void RequestInterruptCall(int bit)
        {
            memory[IF] = (byte)((memory[IF] | (1 << bit)) & 0x1F);
            if (bit != 0)
            {
                // Console.WriteLine($"Interrupt requested bit {bit}, IF now {memory[IF]:X2}");
            }
        }

        /// <summary>
        /// Reads a byte for an OAM DMA transfer.
        /// On DMG/SGB hardware, DMA source reads from E000-FFFF mirror C000-DFFF.
        /// This is different from normal CPU reads, so DMA uses this separate method.
        /// </summary>
        private byte ReadOAMDMA(ushort address)
        {
            if (address >= 0xE000) address = (ushort)(address - 0x2000);
            if (address <= 0x7FFF && ROMCartrige != null) return ROMCartrige.ReadByte(address);
            if (address >= 0xA000 && address <= 0xBFFF && ROMCartrige != null) return ROMCartrige.ReadByte(address);
            return memory[address];
        }
        private bool BitClear(byte value, int bit)
        {
            return (value & (1 << bit)) == 0;
        }
        private byte ReadInputFromJoypad()
        {
            byte selectInput = memory[JOYPAD];
            bool selectDpad = BitClear(selectInput, 4);
            bool selectButtons = BitClear(selectInput, 5);
            byte result = (byte)(0xC0 | (selectInput & 0x30) | 0x0F);
            if (joypad == null) return result;
            if (selectDpad)
            {
                if (joypad.IsRightPressed()) result &= 0xFE;
                if (joypad.IsLeftPressed()) result &= 0xFD;
                if (joypad.IsUpPressed()) result &= 0xFB;
                if (joypad.IsDownPressed()) result &= 0xF7;
            }

            if (selectButtons)
            {
                if (joypad.IsAPressed()) result &= 0xFE;
                if (joypad.IsBPressed()) result &= 0xFD;
                if (joypad.IsSelectPressed()) result &= 0xFB;
                if (joypad.IsStartPressed()) result &= 0xF7;
            }
            return result;
        }

        // Internal timer state.
        // DIV is backed by a 16-bit internal counter. TIMA increments on the falling
        // edge of one selected divider bit, not by simply counting fixed thresholds.
        private int divCounter = 0;
        private bool timerOverflowPending = false;
        private int timerOverflowDelay = 0;

        /// <summary>
        /// Returns the divider bit used by TIMA based on TAC frequency bits.
        /// TIMA increments when this selected bit changes from 1 to 0.
        /// </summary>
        private int GetTimerBit()
        {
            switch (memory[TAC] & 0x03)
            {
                case 0x00: return 9; // 4096 Hz
                case 0x01: return 3; // 262144 Hz
                case 0x02: return 5; // 65536 Hz
                case 0x03: return 7; // 16384 Hz
            }
            return 9;
        }
        private bool GetTimerSignal()
        {
            bool timerEnabled = (memory[TAC] & 0x04) != 0;
            if (!timerEnabled) return false;
            int bit = GetTimerBit();
            return (divCounter & (1 << bit)) != 0;
        }

        /// <summary>
        /// Increments TIMA and starts the delayed overflow reload if TIMA overflows.
        /// The real Game Boy does not reload TIMA from TMA instantly.
        /// </summary>
        private void IncrementTIMA()
        {
            if (timerOverflowPending) return;
            if (memory[TIMA] == 0xFF)
            {
                memory[TIMA] = 0x00;
                timerOverflowPending = true;
                timerOverflowDelay = 4;
            }
            else memory[TIMA]++;
        }

        /// <summary>
        /// Advances an active OAM DMA transfer.
        /// One byte is copied every 4 CPU cycles after the initial start delay.
        /// The full transfer copies 0xA0 bytes into FE00-FE9F.
        /// </summary>
        private void StepOAMDMA(int cycles)
        {
            if (!OAMDMAActive) return;
            while (OAMDMAActive == true && cycles > 0)
            {
                if (OAMDMAStartDelay > 0)
                {
                    int usedcycle = Math.Min(cycles, OAMDMAStartDelay);
                    OAMDMAStartDelay -= usedcycle;
                    cycles -= usedcycle;
                    if (cycles == 0) return;
                }
                OAMDMACycleCounter += cycles;
                cycles = 0;
                while (OAMDMACycleCounter >= 4)
                {
                    OAMDMACycleCounter -= 4;
                    byte DMAByte = ReadOAMDMA((ushort)(OAMDMASource + OAMDMAIndex));
                    memory[0xFE00 + OAMDMAIndex] = DMAByte;//Range begginning 0xFE00
                    OAMDMAIndex++;
                    if (OAMDMAIndex >= 0xA0)//0xA0 Bytes copied between FE00-FE9F
                    {
                        OAMDMAActive = false;
                        OAMDMACycleCounter = 0;
                        OAMDMAStartDelay = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Steps timer-related hardware by the given number of CPU cycles.
        /// This also advances OAM DMA because it is cycle-based hardware behaviour.
        /// </summary>
        public void StepTimers(int cycles)
        {
            StepOAMDMA(cycles);
            for (int i = 0; i < cycles; i++)
            {

                if (timerReloadingCyclesLeft > 0)
                {
                    timerReloadingCyclesLeft--;
                    if (timerReloadingCyclesLeft == 0)
                    {
                        timerReloading = false;
                    }
                }
                if (timerOverflowPending)
                {
                    timerOverflowDelay--;

                    if (timerOverflowDelay <= 0)
                    {
                        memory[TIMA] = memory[TMA];
                        RequestInterruptCall(2);
                        timerOverflowPending = false;
                        // Keeps a short reload-state window so TIMA/TMA writes can behave correctly.
                        timerReloading = true;
                        timerReloadingCyclesLeft = 4;
                    }
                }

                bool oldSignal = GetTimerSignal();

                divCounter = (divCounter + 1) & 0xFFFF;
                memory[DIV] = (byte)(divCounter >> 8);

                bool newSignal = GetTimerSignal();

                if (oldSignal && !newSignal)
                {
                    IncrementTIMA();
                }
            }
        }


        public string GetMBCType()
        {
            return ROMCartrige.GetROMType();
        }

        /// <summary>
        /// Saves memory and timer state for save states.
        /// Hardware timing state must be saved so loading resumes accurately.
        /// </summary>
        public void Save(BinaryWriter stateWriter)
        {
            //Contents of Memory
            stateWriter.Write(memory.Length);
            stateWriter.Write(memory);

            //Timers
            stateWriter.Write(divCounter);
            stateWriter.Write(timerOverflowPending);
            stateWriter.Write(timerOverflowDelay);
            stateWriter.Write(timerReloading);
            stateWriter.Write(timerReloadingCyclesLeft);

            //OAMDMA
            stateWriter.Write(OAMDMAActive);
            stateWriter.Write(OAMDMASource);
            stateWriter.Write(OAMDMAIndex);
            stateWriter.Write(OAMDMACycleCounter);
            stateWriter.Write(OAMDMAStartDelay);
        }

        /// <summary>
        /// Restores memory and timer state from a save state.
        /// </summary>
        public void Load(BinaryReader stateReader)
        {
            int length = stateReader.ReadInt32();
            if (length != memory.Length)
            {
                throw new InvalidDataException("Save state memory size is wrong.");
            }
            byte[] loadedMemory = stateReader.ReadBytes(length);
            if (loadedMemory.Length != length)
            {
                throw new EndOfStreamException("Save state ended while reading memory.");
            }
            Array.Copy(loadedMemory, memory, length);

            divCounter = stateReader.ReadInt32();
            timerOverflowPending = stateReader.ReadBoolean();
            timerOverflowDelay = stateReader.ReadInt32();
            timerReloading = stateReader.ReadBoolean();
            timerReloadingCyclesLeft = stateReader.ReadInt32();
            memory[DIV] = (byte)(divCounter >> 8);

            OAMDMAActive = stateReader.ReadBoolean();
            OAMDMASource = stateReader.ReadUInt16();
            OAMDMAIndex = stateReader.ReadInt32();
            OAMDMACycleCounter = stateReader.ReadInt32();
            OAMDMAStartDelay = stateReader.ReadInt32();
        }
    }

}
