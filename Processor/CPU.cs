using GameBoyEmu.Audio;
using GameBoyEmu.Debugger;
using GameBoyEmu.Main;
using GameBoyEmu.Memory;
using GameBoyEmu.Visual;
using System;
using System.IO;

namespace GameBoyEmu.Processor
{
    public class CPU : ISaveState
    {
        private OpcodeNames opcodeNames = new OpcodeNames();
        public OpcodeQueue opcodeQueue = new OpcodeQueue();
        private Registers _Registers;
        private int debugCounter = 0;
        private int cyclesUsedThisInstruction = 0;
        private GPU gpu;
        private APU apu;
        private MEMORY memory;
        public Registers Registers
        {
            get { return _Registers; }
        }

        private bool IME = false;
        private int imeEnableDelay = 0;
        private bool haltBug = false;
        public CPU(Registers registers, MEMORY Memory)
        {
            _Registers = registers;
            memory = Memory;
        }
        public void ConnectHardware(GPU gpuIn, APU apuIn)
        {
            gpu = gpuIn;
            apu = apuIn;
        }

        private void Tick(int cyclesIn)
        {
            cyclesUsedThisInstruction += cyclesIn;
            if (gpu != null)
            {
                gpu.Step(cyclesIn);
            }
            if (apu != null)
            {
                apu.Step(cyclesIn);
            }
            memory.StepTimers(cyclesIn);
        }

        private byte Fetch8Bit()
        {
            byte value = memory.ReadByteFromAddress(_Registers.PC);
            _Registers.PC++;
            Tick(4);
            return value;
        }

        private byte Fetch()
        {
            byte value = memory.ReadByteFromAddress(_Registers.PC);
            opcodeQueue.Enqueue($"{Registers.PC:X4}: {value:X2} ({opcodeNames.GetOpCodeName(value)})");
            if (haltBug) haltBug = false;
            else _Registers.PC++;
            Tick(4);
            return value;
        }
        private ushort Fetch16Bit()
        {
            byte lowByte = Fetch8Bit();
            byte highByte = Fetch8Bit();
            return (ushort)(lowByte | (highByte << 8));
        }

        private byte Read8Bit(ushort address)
        {
            Tick(4);

            if (address >= 0xFE00 && address <= 0xFEFF)
            {
                TriggerOAMBugRead(address);
            }

            return memory.ReadByteFromAddress(address);
        }

        private void Write8Bit(ushort address, byte value)
        {
            memory.WriteByteToAddress(address, value);
            Tick(4);

            if (address >= 0xFE00 && address <= 0xFEFF)
            {
                TriggerOAMBugWrite(address);
            }
        }

        private void Miscallaneous()
        {
            Tick(4);
        }
        private void TriggerOAMBugIDU(ushort address)
        {
            if (gpu == null) return;

            memory.TriggerOAMBugIDU(address, gpu.scanlineCycles);
        }
        private void TriggerOAMBugRead(ushort address)
        {
            if (gpu == null) return;
            memory.TriggerOAMBugRead(address, gpu.scanlineCycles);
        }

        private void TriggerOAMBugWrite(ushort address)
        {
            if (gpu == null) return;
            memory.TriggerOAMBugWrite(address, gpu.scanlineCycles);
        }
        public int Cycle()
        {
            cyclesUsedThisInstruction = 0;
            debugCounter++;
            int interruptCycles = HandleInterrupts();
            if (interruptCycles != 0)
            {
                while (cyclesUsedThisInstruction < interruptCycles)
                {
                    Tick(4);
                }
                return cyclesUsedThisInstruction;
            }
            if (_Registers.HALT)
            {
                if (HasPendingInterrupt())
                {
                    _Registers.HALT = false;
                }
                else
                {
                    Tick(4);
                    return cyclesUsedThisInstruction;
                }
            }
            byte nextOpCode = Fetch();
            int expectedCycles = Execute(nextOpCode);

            if (imeEnableDelay > 0)
            {
                imeEnableDelay--;

                if (imeEnableDelay == 0)
                    IME = true;
            }
            while (cyclesUsedThisInstruction < expectedCycles)
            {
                Tick(4);
            }
            return cyclesUsedThisInstruction;
        }

        private int HandleInterrupts()
        {
            byte requested = memory.ReadByteFromAddress(MEMORY.IF);
            byte enabled = memory.ReadByteFromAddress(MEMORY.IE);
            byte pending = (byte)(requested & enabled & 0x1F);
            if (pending == 0) return 0;
            _Registers.HALT = false;
            if (!IME) return 0;
            IME = false;

            if ((pending & 0x01) != 0) return ServiceInterrupt(0, 0x0040); // VBlank
            if ((pending & 0x02) != 0) return ServiceInterrupt(1, 0x0048); // LCD STAT
            if ((pending & 0x04) != 0) return ServiceInterrupt(2, 0x0050); // Timer
            if ((pending & 0x08) != 0) return ServiceInterrupt(3, 0x0058); // Serial
            if ((pending & 0x10) != 0) return ServiceInterrupt(4, 0x0060); // Joypad
            return 0;
        }

        private int ServiceInterrupt(int bit, ushort vector)
        {
            _Registers.HALT = false;
            Miscallaneous();
            Miscallaneous();
            byte flags = memory.ReadByteFromAddress(MEMORY.IF);
            flags &= (byte)~(1 << bit);
            memory.ForceWriteByte(MEMORY.IF, flags);
            _Registers.SP--;
            Write8Bit(_Registers.SP, (byte)(_Registers.PC >> 8));
            _Registers.SP--;
            Write8Bit(_Registers.SP, (byte)(_Registers.PC & 0xFF));
            Miscallaneous();
            _Registers.PC = vector;
            return 20;
        }
        public void JumpRelative(bool cond)
        {
            sbyte memoryOffSet = (sbyte)Fetch8Bit();
            if (cond == true)
            {
                _Registers.PC = (ushort)(_Registers.PC + memoryOffSet);
                Miscallaneous();
            }
        }

        public void Push(ushort value)
        {
            Miscallaneous();
            TriggerOAMBugIDU(_Registers.SP);
            _Registers.SP--;
            Write8Bit(_Registers.SP, (byte)(value >> 8));
            TriggerOAMBugIDU(_Registers.SP);
            _Registers.SP--;
            Write8Bit(_Registers.SP, (byte)(value & 0xFF));
        }

        public ushort Pop()
        {
            ushort oldSP = _Registers.SP;
            Tick(4);
            if (oldSP >= 0xFE00 && oldSP <= 0xFEFF && gpu != null)
            {
                memory.TriggerOAMBugReadIDU(oldSP, gpu.scanlineCycles);
            }
            byte lowByte = memory.ReadByteFromAddress(_Registers.SP);
            _Registers.SP++;
            byte highByte = Read8Bit(_Registers.SP);
            _Registers.SP++;
            return (ushort)((highByte << 8) | lowByte);
        }

        private bool HasPendingInterrupt()
        {
            return (memory.ReadByteFromAddress(MEMORY.IF) &
                    memory.ReadByteFromAddress(MEMORY.IE) & 0x1F) != 0;
        }


        public int Execute(byte opCode)
        {
            switch (opCode)
            {
                //NOP
                case 0x00:
                    return 4;
                //HALT
                case 0x76:
                    if (!IME && HasPendingInterrupt())
                    {
                        haltBug = true;
                        _Registers.HALT = false;
                    }
                    else
                    {
                        _Registers.HALT = true;
                    }
                    return 4;
                //STOP
                case 0x10:
                    _Registers.PC++;
                    return 4;
                //LOADS FROM MEMORY
                case 0x3E:
                    _Registers.A = Fetch8Bit();
                    return 8;
                case 0x06:
                    _Registers.B = Fetch8Bit();
                    return 8;
                case 0x0E:
                    _Registers.C = Fetch8Bit();
                    return 8;
                case 0x16:
                    _Registers.D = Fetch8Bit();
                    return 8;
                case 0x1E:
                    _Registers.E = Fetch8Bit();
                    return 8;
                case 0x26:
                    _Registers.H = Fetch8Bit();
                    return 8;
                case 0x2E:
                    _Registers.L = Fetch8Bit();
                    return 8;
                //THIS LOAD ONLY HAS THE RANGE 0xFF00-0xFFFF
                case 0xF0:
                    {
                        byte offset = Fetch8Bit();
                        ushort address = (ushort)(0xFF00 + offset);
                        _Registers.A = Read8Bit(address);
                        return 12;
                    }
                case 0xFA:
                    {
                        ushort address = Fetch16Bit();
                        _Registers.A = Read8Bit(address);
                        return 16;
                    }
                case 0x0A:
                    _Registers.A = Read8Bit(_Registers.BC);
                    return 8;
                case 0x1A:
                    _Registers.A = Read8Bit(_Registers.DE);
                    return 8;
                //16-BIT LOADS
                case 0x01:
                    _Registers.BC = Fetch16Bit();
                    return 12;
                case 0x11:
                    _Registers.DE = Fetch16Bit();
                    return 12;
                case 0x21:
                    _Registers.HL = Fetch16Bit();
                    return 12;
                case 0x31:
                    _Registers.SP = Fetch16Bit();
                    return 12;
                //LOADS FROM REGISTER TO REGISTER
                case 0x7F:
                    _Registers.A = _Registers.A;
                    return 4;
                case 0x78:
                    _Registers.A = _Registers.B;
                    return 4;
                case 0x79:
                    _Registers.A = _Registers.C;
                    return 4;
                case 0x7A:
                    _Registers.A = _Registers.D;
                    return 4;
                case 0x7B:
                    _Registers.A = _Registers.E;
                    return 4;
                case 0x7C:
                    _Registers.A = _Registers.H;
                    return 4;
                case 0x7D:
                    _Registers.A = _Registers.L;
                    return 4;
                case 0x7E:
                    _Registers.A = Read8Bit(_Registers.HL);
                    return 8;
                case 0x47:
                    _Registers.B = _Registers.A;
                    return 4;
                case 0x40:
                    _Registers.B = _Registers.B;
                    return 4;
                case 0x41:
                    _Registers.B = _Registers.C;
                    return 4;
                case 0x42:
                    _Registers.B = _Registers.D;
                    return 4;
                case 0x43:
                    _Registers.B = _Registers.E;
                    return 4;
                case 0x44:
                    _Registers.B = _Registers.H;
                    return 4;
                case 0x45:
                    _Registers.B = _Registers.L;
                    return 4;
                case 0x46:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.B = operand;
                        return 8;
                    }
                case 0x4F:
                    _Registers.C = _Registers.A;
                    return 4;
                case 0x48:
                    _Registers.C = _Registers.B;
                    return 4;
                case 0x49:
                    _Registers.C = _Registers.C;
                    return 4;
                case 0x4A:
                    _Registers.C = _Registers.D;
                    return 4;
                case 0x4B:
                    _Registers.C = _Registers.E;
                    return 4;
                case 0x4C:
                    _Registers.C = _Registers.H;
                    return 4;
                case 0x4D:
                    _Registers.C = _Registers.L;
                    return 4;
                case 0x4E:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.C = operand;
                        return 8;
                    }
                case 0x57:
                    _Registers.D = _Registers.A;
                    return 4;
                case 0x50:
                    _Registers.D = _Registers.B;
                    return 4;
                case 0x51:
                    _Registers.D = _Registers.C;
                    return 4;
                case 0x52:
                    _Registers.D = _Registers.D;
                    return 4;
                case 0x53:
                    _Registers.D = _Registers.E;
                    return 4;
                case 0x54:
                    _Registers.D = _Registers.H;
                    return 4;
                case 0x55:
                    _Registers.D = _Registers.L;
                    return 4;
                case 0x56:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.D = operand;
                        return 8;
                    }
                case 0x5F:
                    _Registers.E = _Registers.A;
                    return 4;
                case 0x58:
                    _Registers.E = _Registers.B;
                    return 4;
                case 0x59:
                    _Registers.E = _Registers.C;
                    return 4;
                case 0x5A:
                    _Registers.E = _Registers.D;
                    return 4;
                case 0x5B:
                    _Registers.E = _Registers.E;
                    return 4;
                case 0x5C:
                    _Registers.E = _Registers.H;
                    return 4;
                case 0x5D:
                    _Registers.E = _Registers.L;
                    return 4;
                case 0x5E:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.E = operand;
                        return 8;
                    }
                case 0x67:
                    _Registers.H = _Registers.A;
                    return 4;
                case 0x60:
                    _Registers.H = _Registers.B;
                    return 4;
                case 0x61:
                    _Registers.H = _Registers.C;
                    return 4;
                case 0x62:
                    _Registers.H = _Registers.D;
                    return 4;
                case 0x63:
                    _Registers.H = _Registers.E;
                    return 4;
                case 0x64:
                    _Registers.H = _Registers.H;
                    return 4;
                case 0x65:
                    _Registers.H = _Registers.L;
                    return 4;
                case 0x66:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.H = operand;
                        return 8;
                    }
                case 0x6F:
                    _Registers.L = _Registers.A;
                    return 4;
                case 0x68:
                    _Registers.L = _Registers.B;
                    return 4;
                case 0x69:
                    _Registers.L = _Registers.C;
                    return 4;
                case 0x6A:
                    _Registers.L = _Registers.D;
                    return 4;
                case 0x6B:
                    _Registers.L = _Registers.E;
                    return 4;
                case 0x6C:
                    _Registers.L = _Registers.H;
                    return 4;
                case 0x6D:
                    _Registers.L = _Registers.L;
                    return 4;
                case 0x6E:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.L = operand;
                        return 8;
                    }
                //LDI
                case 0x2A:
                    ushort oldHL = _Registers.HL;
                    Tick(4);
                    if (oldHL >= 0xFE00 && oldHL <= 0xFEFF && gpu != null)
                    {
                        memory.TriggerOAMBugReadIDU(oldHL, gpu.scanlineCycles);
                    }
                    _Registers.A = memory.ReadByteFromAddress(oldHL);
                    _Registers.HL++;
                    return 8;
                case 0x22:
                    Write8Bit(_Registers.HL, _Registers.A);
                    _Registers.HL++;
                    return 8;
                //LDD
                case 0x32:
                    Write8Bit(_Registers.HL, _Registers.A);
                    _Registers.HL--;
                    return 8;
                case 0x3A:
                    oldHL = _Registers.HL;
                    Tick(4);
                    if (oldHL >= 0xFE00 && oldHL <= 0xFEFF && gpu != null)
                    {
                        memory.TriggerOAMBugReadIDU(oldHL, gpu.scanlineCycles);
                    }
                    _Registers.A = memory.ReadByteFromAddress(oldHL);
                    _Registers.HL--;
                    return 8;
                //WRITES TO MEMORY
                case 0xEA:
                    {
                        ushort address = Fetch16Bit();
                        Write8Bit(address, _Registers.A);
                        return 16;
                    }
                case 0xE0:
                    {
                        byte offset = Fetch8Bit();
                        ushort address = (ushort)(0xFF00 + offset);
                        Write8Bit(address, _Registers.A);
                        return 12;
                    }
                case 0x36:
                    {
                        byte operand = Fetch8Bit();
                        Write8Bit(_Registers.HL, operand);
                        return 12;
                    }
                case 0x77:
                    Write8Bit(_Registers.HL, _Registers.A);
                    return 8;
                case 0x70:
                    Write8Bit(_Registers.HL, _Registers.B);
                    return 8;
                case 0x71:
                    Write8Bit(_Registers.HL, _Registers.C);
                    return 8;
                case 0x72:
                    Write8Bit(_Registers.HL, _Registers.D);
                    return 8;
                case 0x73:
                    Write8Bit(_Registers.HL, _Registers.E);
                    return 8;
                case 0x74:
                    Write8Bit(_Registers.HL, _Registers.H);
                    return 8;
                case 0x75:
                    Write8Bit(_Registers.HL, _Registers.L);
                    return 8;

                case 0x02:
                    Write8Bit(_Registers.BC, _Registers.A);
                    return 8;
                case 0x12:
                    Write8Bit(_Registers.DE, _Registers.A);
                    return 8;



                // ADDITIONS
                case 0xC6:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.Addition(operand);
                        return 8;
                    }
                case 0x87:
                    _Registers.Addition(_Registers.A);
                    return 4;
                case 0x80:
                    _Registers.Addition(_Registers.B);
                    return 4;
                case 0x81:
                    _Registers.Addition(_Registers.C);
                    return 4;
                case 0x82:
                    _Registers.Addition(_Registers.D);
                    return 4;
                case 0x83:
                    _Registers.Addition(_Registers.E);
                    return 4;
                case 0x84:
                    _Registers.Addition(_Registers.H);
                    return 4;
                case 0x85:
                    _Registers.Addition(_Registers.L);
                    return 4;
                case 0x86:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.Addition(operand);
                        return 8;
                    }

                //16-BIT ADDITIONS
                case 0x09:
                    _Registers.WordAddition(_Registers.BC);
                    return 8;
                case 0x19:
                    _Registers.WordAddition(_Registers.DE);
                    return 8;
                case 0x29:
                    _Registers.WordAddition(_Registers.HL);
                    return 8;
                case 0x39:
                    _Registers.WordAddition(_Registers.SP);
                    return 8;

                //ADC ADDITIONS
                case 0xCE:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.AdditionWithCarry(operand);
                        return 8;
                    }
                case 0x8F:
                    _Registers.AdditionWithCarry(_Registers.A);
                    return 4;
                case 0x88:
                    _Registers.AdditionWithCarry(_Registers.B);
                    return 4;
                case 0x89:
                    _Registers.AdditionWithCarry(_Registers.C);
                    return 4;
                case 0x8A:
                    _Registers.AdditionWithCarry(_Registers.D);
                    return 4;
                case 0x8B:
                    _Registers.AdditionWithCarry(_Registers.E);
                    return 4;
                case 0x8C:
                    _Registers.AdditionWithCarry(_Registers.H);
                    return 4;
                case 0x8D:
                    _Registers.AdditionWithCarry(_Registers.L);
                    return 4;
                case 0x8E:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.AdditionWithCarry(operand);
                        return 8;
                    }
                // SUBTRACTIONS
                case 0xD6:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.Subtraction(operand);
                        return 8;
                    }
                case 0x97:
                    _Registers.Subtraction(_Registers.A);
                    return 4;
                case 0x90:
                    _Registers.Subtraction(_Registers.B);
                    return 4;
                case 0x91:
                    _Registers.Subtraction(_Registers.C);
                    return 4;
                case 0x92:
                    _Registers.Subtraction(_Registers.D);
                    return 4;
                case 0x93:
                    _Registers.Subtraction(_Registers.E);
                    return 4;
                case 0x94:
                    _Registers.Subtraction(_Registers.H);
                    return 4;
                case 0x95:
                    _Registers.Subtraction(_Registers.L);
                    return 4;
                case 0x96:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.Subtraction(operand);
                        return 8;
                    }

                //SBC SUBTRACTIONS
                case 0xDE:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.SubtractionWithCarry(operand);
                        return 8;
                    }
                case 0x9F:
                    _Registers.SubtractionWithCarry(_Registers.A);
                    return 4;
                case 0x98:
                    _Registers.SubtractionWithCarry(_Registers.B);
                    return 4;
                case 0x99:
                    _Registers.SubtractionWithCarry(_Registers.C);
                    return 4;
                case 0x9A:
                    _Registers.SubtractionWithCarry(_Registers.D);
                    return 4;
                case 0x9B:
                    _Registers.SubtractionWithCarry(_Registers.E);
                    return 4;
                case 0x9C:
                    _Registers.SubtractionWithCarry(_Registers.H);
                    return 4;
                case 0x9D:
                    _Registers.SubtractionWithCarry(_Registers.L);
                    return 4;
                case 0x9E:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.SubtractionWithCarry(operand);
                        return 8;
                    }

                //INCREMENTS 8-BIT
                case 0x3C:
                    _Registers.Increment(ref _Registers.A);
                    return 4;
                case 0x04:
                    _Registers.Increment(ref _Registers.B);
                    return 4;
                case 0x0C:
                    _Registers.Increment(ref _Registers.C);
                    return 4;
                case 0x14:
                    _Registers.Increment(ref _Registers.D);
                    return 4;
                case 0x1C:
                    _Registers.Increment(ref _Registers.E);
                    return 4;
                case 0x24:
                    _Registers.Increment(ref _Registers.H);
                    return 4;
                case 0x2C:
                    _Registers.Increment(ref _Registers.L);
                    return 4;
                case 0x34:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        int result = operand + 1;
                        _Registers.UpdateFlag(Registers.Flags.Z, (byte)result == 0);
                        _Registers.UpdateFlag(Registers.Flags.N, false);
                        _Registers.UpdateFlag(Registers.Flags.H, (operand & 0x0F) + 1 > 0x0F);
                        Write8Bit(_Registers.HL, (byte)result);
                        return 12;
                    }
                //INCREMENTS 16-BIT
                case 0x03:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.BC);
                    _Registers.BC++;
                    return 8;
                case 0x13:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.DE);
                    _Registers.DE++;
                    return 8;
                case 0x23:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.HL);
                    _Registers.HL++;
                    return 8;
                case 0x33:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.SP);
                    _Registers.SP++;
                    return 8;
                //DECREMENT 8-BIT
                case 0x3D:
                    _Registers.Decrement(ref _Registers.A);
                    return 4;
                case 0x05:
                    _Registers.Decrement(ref _Registers.B);
                    return 4;
                case 0x0D:
                    _Registers.Decrement(ref _Registers.C);
                    return 4;
                case 0x15:
                    _Registers.Decrement(ref _Registers.D);
                    return 4;
                case 0x1D:
                    _Registers.Decrement(ref _Registers.E);
                    return 4;
                case 0x25:
                    _Registers.Decrement(ref _Registers.H);
                    return 4;
                case 0x2D:
                    _Registers.Decrement(ref _Registers.L);
                    return 4;
                case 0x35:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        int result = operand - 1;
                        _Registers.UpdateFlag(Registers.Flags.Z, result == 0);
                        _Registers.UpdateFlag(Registers.Flags.N, true);
                        _Registers.UpdateFlag(Registers.Flags.H, (operand & 0x0F) == 0);
                        Write8Bit(_Registers.HL, (byte)result);
                        return 12;
                    }
                //DECREMENT 16-BIT
                case 0x0B:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.BC);
                    _Registers.BC--;
                    return 8;
                case 0x1B:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.DE);
                    _Registers.DE--;
                    return 8;
                case 0x2B:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.HL);
                    _Registers.HL--;
                    return 8;
                case 0x3B:
                    Miscallaneous();
                    TriggerOAMBugIDU(_Registers.SP);
                    _Registers.SP--;
                    return 8;
                //EQUALITY COMPARISONS
                case 0xFE:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.Comp(operand);
                        return 8;
                    }
                case 0xBF:
                    _Registers.Comp(_Registers.A);
                    return 4;
                case 0xB8:
                    _Registers.Comp(_Registers.B);
                    return 4;
                case 0xB9:
                    _Registers.Comp(_Registers.C);
                    return 4;
                case 0xBA:
                    _Registers.Comp(_Registers.D);
                    return 4;
                case 0xBB:
                    _Registers.Comp(_Registers.E);
                    return 4;
                case 0xBC:
                    _Registers.Comp(_Registers.H);
                    return 4;
                case 0xBD:
                    _Registers.Comp(_Registers.L);
                    return 4;
                case 0xBE:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.Comp(operand);
                        return 8;
                    }
                //BITWISE AND
                case 0xE6:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.BitwiseAND(operand);
                        return 8;
                    }
                case 0xA7:
                    _Registers.BitwiseAND(_Registers.A);
                    return 4;
                case 0xA0:
                    _Registers.BitwiseAND(_Registers.B);
                    return 4;
                case 0xA1:
                    _Registers.BitwiseAND(_Registers.C);
                    return 4;
                case 0xA2:
                    _Registers.BitwiseAND(_Registers.D);
                    return 4;
                case 0xA3:
                    _Registers.BitwiseAND(_Registers.E);
                    return 4;
                case 0xA4:
                    _Registers.BitwiseAND(_Registers.H);
                    return 4;
                case 0xA5:
                    _Registers.BitwiseAND(_Registers.L);
                    return 4;
                case 0xA6:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.BitwiseAND(operand);
                        return 8;
                    }
                //BITWISE OR
                case 0xF6:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.BitwiseOR(operand);
                        return 8;
                    }
                case 0xB7:
                    _Registers.BitwiseOR(_Registers.A);
                    return 4;
                case 0xB0:
                    _Registers.BitwiseOR(_Registers.B);
                    return 4;
                case 0xB1:
                    _Registers.BitwiseOR(_Registers.C);
                    return 4;
                case 0xB2:
                    _Registers.BitwiseOR(_Registers.D);
                    return 4;
                case 0xB3:
                    _Registers.BitwiseOR(_Registers.E);
                    return 4;
                case 0xB4:
                    _Registers.BitwiseOR(_Registers.H);
                    return 4;
                case 0xB5:
                    _Registers.BitwiseOR(_Registers.L);
                    return 4;
                case 0xB6:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.BitwiseOR(operand);
                        return 8;
                    }
                //BITWISE XOR
                case 0xEE:
                    {
                        byte operand = Fetch8Bit();
                        _Registers.BitwiseXOR(operand);
                        return 8;
                    }
                case 0xAF:
                    _Registers.BitwiseXOR(_Registers.A);
                    return 4;
                case 0xA8:
                    _Registers.BitwiseXOR(_Registers.B);
                    return 4;
                case 0xA9:
                    _Registers.BitwiseXOR(_Registers.C);
                    return 4;
                case 0xAA:
                    _Registers.BitwiseXOR(_Registers.D);
                    return 4;
                case 0xAB:
                    _Registers.BitwiseXOR(_Registers.E);
                    return 4;
                case 0xAC:
                    _Registers.BitwiseXOR(_Registers.H);
                    return 4;
                case 0xAD:
                    _Registers.BitwiseXOR(_Registers.L);
                    return 4;
                case 0xAE:
                    {
                        byte operand = Read8Bit(_Registers.HL);
                        _Registers.BitwiseXOR(operand);
                        return 8;
                    }
                //JUMPS
                case 0x18:
                    JumpRelative(true);
                    return 12;
                case 0x28:
                    bool ZFlag = _Registers.CheckFlag(Registers.Flags.Z);
                    JumpRelative(ZFlag);
                    return ZFlag ? 12 : 8;
                case 0x20:
                    bool notZFlag = !_Registers.CheckFlag(Registers.Flags.Z);
                    JumpRelative(notZFlag);
                    return notZFlag ? 12 : 8;
                case 0x38:
                    bool CFlag = _Registers.CheckFlag(Registers.Flags.C);
                    JumpRelative(CFlag);
                    return CFlag ? 12 : 8;
                case 0x30:
                    bool notCFlag = !_Registers.CheckFlag(Registers.Flags.C);
                    JumpRelative(notCFlag);
                    return notCFlag ? 12 : 8;
                case 0xC3:
                    {
                        ushort address = Fetch16Bit();
                        _Registers.PC = address;
                        Miscallaneous();
                        return 16;
                    }
                case 0xE9:
                    _Registers.PC = _Registers.HL;
                    return 4;
                //JUMPS ADDRESS
                case 0xCA:
                    {
                        ushort address = Fetch16Bit();
                        if (_Registers.CheckFlag(Registers.Flags.Z))
                        {
                            _Registers.PC = address;
                            Miscallaneous();
                            return 16;
                        }
                        return 12;
                    }
                case 0xC2:
                    {
                        ushort address = Fetch16Bit();
                        if (!_Registers.CheckFlag(Registers.Flags.Z))
                        {
                            _Registers.PC = address;
                            Miscallaneous();
                            return 16;
                        }
                        return 12;
                    }
                case 0xDA:
                    {
                        ushort address = Fetch16Bit();
                        if (_Registers.CheckFlag(Registers.Flags.C))
                        {
                            _Registers.PC = address;
                            Miscallaneous();
                            return 16;
                        }
                        return 12;
                    }
                case 0xD2:
                    {
                        ushort address = Fetch16Bit();
                        if (!_Registers.CheckFlag(Registers.Flags.C))
                        {
                            _Registers.PC = address;
                            Miscallaneous();
                            return 16;
                        }
                        return 12;
                    }
                //PUSH
                case 0xF5:
                    Push(_Registers.AF);
                    return 16;
                case 0xC5:
                    Push(_Registers.BC);
                    return 16;
                case 0xD5:
                    Push(_Registers.DE);
                    return 16;
                case 0xE5:
                    Push(_Registers.HL);
                    return 16;
                //POP
                case 0xF1:
                    _Registers.AF = Pop();
                    return 12;
                case 0xC1:
                    _Registers.BC = Pop();
                    return 12;
                case 0xD1:
                    _Registers.DE = Pop();
                    return 12;
                case 0xE1:
                    _Registers.HL = Pop();
                    return 12;
                //CALL
                case 0xCD:
                    {
                        ushort address = Fetch16Bit();
                        Push(_Registers.PC);
                        _Registers.PC = address;
                        return 24;
                    }
                case 0xC4:
                    {
                        ushort address = Fetch16Bit();
                        if (!_Registers.CheckFlag(Registers.Flags.Z))
                        {
                            Push(_Registers.PC);
                            _Registers.PC = address;
                            return 24;
                        }
                        return 12;
                    }
                case 0xD4:
                    {
                        ushort address = Fetch16Bit();
                        if (!_Registers.CheckFlag(Registers.Flags.C))
                        {
                            Push(_Registers.PC);
                            _Registers.PC = address;
                            return 24;
                        }
                        return 12;
                    }
                case 0xCC:
                    {
                        ushort address = Fetch16Bit();
                        if (_Registers.CheckFlag(Registers.Flags.Z))
                        {
                            Push(_Registers.PC);
                            _Registers.PC = address;
                            return 24;
                        }
                        return 12;
                    }
                case 0xDC:
                    {
                        ushort address = Fetch16Bit();
                        if (_Registers.CheckFlag(Registers.Flags.C))
                        {
                            Push(_Registers.PC);
                            _Registers.PC = address;
                            return 24;
                        }
                        return 12;
                    }
                //RET
                case 0xC8:
                    Miscallaneous();
                    if (_Registers.CheckFlag(Registers.Flags.Z))
                    {
                        _Registers.PC = Pop();
                        Miscallaneous();
                        return 20;
                    }
                    return 8;
                case 0xD8:
                    Miscallaneous();
                    if (_Registers.CheckFlag(Registers.Flags.C))
                    {
                        _Registers.PC = Pop();
                        Miscallaneous();
                        return 20;
                    }
                    return 8;
                case 0xC9:
                    _Registers.PC = Pop();
                    Miscallaneous();
                    return 16;
                case 0xC0:
                    Miscallaneous();
                    if (!_Registers.CheckFlag(Registers.Flags.Z))
                    {
                        _Registers.PC = Pop();
                        Miscallaneous();
                        return 20;
                    }
                    return 8;
                case 0xD0:
                    Miscallaneous();
                    if (!_Registers.CheckFlag(Registers.Flags.C))
                    {
                        _Registers.PC = Pop();
                        Miscallaneous();
                        return 20;
                    }
                    return 8;
                //RST
                case 0xC7:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0000;
                    return 16;
                case 0xCF:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0008;
                    return 16;
                case 0xD7:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0010;
                    return 16;
                case 0xDF:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0018;
                    return 16;
                case 0xE7:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0020;
                    return 16;
                case 0xEF:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0028;
                    return 16;
                case 0xF7:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0030;
                    return 16;
                case 0xFF:
                    Push(_Registers.PC);
                    _Registers.PC = 0x0038;
                    return 16;
                //INTERRUPTS
                case 0xF3:
                    IME = false;
                    imeEnableDelay = 0;
                    return 4;
                case 0xFB:
                    imeEnableDelay = 2;
                    return 4;
                //LD (C), A
                case 0xE2:
                    {
                        ushort address = (ushort)(0xFF00 + _Registers.C);
                        Write8Bit(address, _Registers.A);
                        return 8;
                    }
                //LDH A, (C)
                case 0xF2:
                    {
                        ushort address = (ushort)(0xFF00 + _Registers.C);
                        _Registers.A = Read8Bit(address);
                        return 8;
                    }
                //LD a16, SP
                case 0x08:
                    {
                        ushort address = Fetch16Bit();
                        byte lowerByte = (byte)(_Registers.SP & 0x00FF);
                        byte upperByte = (byte)(_Registers.SP >> 8);
                        Write8Bit(address, lowerByte);
                        Write8Bit((ushort)(address + 1), upperByte);
                        return 20;
                    }
                //CPL 
                case 0x2F:
                    _Registers.A = (byte)~_Registers.A;
                    _Registers.UpdateFlag(Registers.Flags.N, true);
                    _Registers.UpdateFlag(Registers.Flags.H, true);
                    return 4;
                //RLA
                case 0x17:
                    {
                        bool carryBefore = _Registers.CheckFlag(Registers.Flags.C);
                        bool bit7 = (_Registers.A & 0x80) != 0;
                        _Registers.A = (byte)((_Registers.A << 1) | (carryBefore ? 1 : 0));
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.C, bit7);
                        return 4;
                    }
                //RRA
                case 0x1F:
                    {
                        bool carryBefore = _Registers.CheckFlag(Registers.Flags.C);
                        bool bit0 = (_Registers.A & 0x01) != 0;
                        _Registers.A = (byte)((_Registers.A >> 1 | (carryBefore ? 0x80 : 0x00)));
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.C, bit0);
                        return 4;
                    }


                //RLCA
                case 0x07:
                    {
                        bool bit7 = ((_Registers.A & 0x80) != 0);
                        _Registers.A = (byte)((_Registers.A << 1) | (bit7 ? 0x01 : 0x00));
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.C, bit7);
                        return 4;
                    }
                //RRCA
                case 0x0F:
                    {
                        bool bit0 = ((_Registers.A & 0x01) != 0);
                        _Registers.A = (byte)((_Registers.A >> 1) | (bit0 ? 0x80 : 0x00));
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.C, bit0);
                        return 4;
                    }
                //RETI
                case 0xD9:
                    _Registers.PC = Pop();
                    IME = true;
                    Miscallaneous();
                    return 16;
                //DAA
                case 0x27:
                    _Registers.DAA();
                    return 4;
                //SCF
                case 0x37:
                    _Registers.UpdateFlag(Registers.Flags.N, false);
                    _Registers.UpdateFlag(Registers.Flags.H, false);
                    _Registers.UpdateFlag(Registers.Flags.C, true);
                    return 4;
                //CCF
                case 0x3F:
                    {
                        bool carryBefore = _Registers.CheckFlag(Registers.Flags.C);
                        _Registers.UpdateFlag(Registers.Flags.N, false);
                        _Registers.UpdateFlag(Registers.Flags.H, false);
                        _Registers.UpdateFlag(Registers.Flags.C, !carryBefore);
                        return 4;
                    }
                //CB opcodes
                case 0xCB:
                    opCode = Fetch8Bit();
                    return ExecuteCBInstruction(opCode);
                //ADD SP, s8
                case 0xE8:
                    {
                        byte rawOffset = Fetch8Bit();
                        sbyte signedOffset = (sbyte)(rawOffset);
                        ushort oldSP = _Registers.SP;
                        int result = oldSP + signedOffset;
                        _Registers.SP = (ushort)result;
                        _Registers.UpdateFlag(Registers.Flags.Z, false);
                        _Registers.UpdateFlag(Registers.Flags.N, false);
                        _Registers.UpdateFlag(Registers.Flags.H, (oldSP & 0x0F) + (rawOffset & 0x0F) > 0x0F);
                        _Registers.UpdateFlag(Registers.Flags.C, ((oldSP & 0xFF) + rawOffset) > 0xFF);
                        Miscallaneous();
                        Miscallaneous();
                        return 16;
                    }
                //LD HL, SP+s8
                case 0xF8:
                    {
                        byte rawOffset = Fetch8Bit();
                        sbyte signedOffset = (sbyte)(rawOffset);
                        ushort oldSP = _Registers.SP;
                        int result = oldSP + signedOffset;
                        _Registers.HL = (ushort)result;
                        _Registers.UpdateFlag(Registers.Flags.Z, false);
                        _Registers.UpdateFlag(Registers.Flags.N, false);
                        Registers.UpdateFlag(Registers.Flags.H, (oldSP & 0x0F) + (rawOffset & 0x0F) > 0x0F);
                        _Registers.UpdateFlag(Registers.Flags.C, ((oldSP & 0xFF) + rawOffset) > 0xFF);
                        Miscallaneous();
                        return 12;
                    }
                //LD SP, HL
                case 0xF9:
                    Miscallaneous();
                    _Registers.SP = _Registers.HL;
                    return 8;


                //DEBUG
                default:
                    Logger.LogEnable = true;
                    Logger.Write($"Unknown opcode: {opCode:X2} at PC:{(_Registers.PC - 1):X4}");
                    Logger.Write(
                    $"A:{_Registers.A:X2} F:{_Registers.F:X2} " +
                    $"B:{_Registers.B:X2} C:{_Registers.C:X2} " +
                    $"D:{_Registers.D:X2} E:{_Registers.E:X2} " +
                    $"H:{_Registers.H:X2} L:{_Registers.L:X2} " +
                    $"SP:{_Registers.SP:X4}"
                    );
                    Logger.LogEnable = false;
                    return 0;
            }



        }
        public int ExecuteCBInstruction(byte opCode)
        {
            switch (opCode)
            {

                //RLC
                case 0x07:
                    _Registers.RLC(ref _Registers.A);
                    return 8;
                case 0x00:
                    _Registers.RLC(ref _Registers.B);
                    return 8;
                case 0x01:
                    _Registers.RLC(ref _Registers.C);
                    return 8;
                case 0x02:
                    _Registers.RLC(ref _Registers.D);
                    return 8;
                case 0x03:
                    _Registers.RLC(ref _Registers.E);
                    return 8;
                case 0x04:
                    _Registers.RLC(ref _Registers.H);
                    return 8;
                case 0x05:
                    _Registers.RLC(ref _Registers.L);
                    return 8;
                case 0x06:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        bool bit7 = ((value & 0x80) != 0);
                        value = (byte)((value << 1) | (bit7 ? 0x01 : 0x00));
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.Z, value == 0);
                        _Registers.UpdateFlag(Registers.Flags.C, bit7);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //RRC
                case 0x0F:
                    _Registers.RRC(ref _Registers.A);
                    return 8;
                case 0x08:
                    _Registers.RRC(ref _Registers.B);
                    return 8;
                case 0x09:
                    _Registers.RRC(ref _Registers.C);
                    return 8;
                case 0x0A:
                    _Registers.RRC(ref _Registers.D);
                    return 8;
                case 0x0B:
                    _Registers.RRC(ref _Registers.E);
                    return 8;
                case 0x0C:
                    _Registers.RRC(ref _Registers.H);
                    return 8;
                case 0x0D:
                    _Registers.RRC(ref _Registers.L);
                    return 8;
                case 0x0E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        bool bit0 = ((value & 0x01) != 0);
                        value = (byte)((value >> 1) | (bit0 ? 0x80 : 0x00));
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.Z, value == 0);
                        _Registers.UpdateFlag(Registers.Flags.C, bit0);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //RL
                case 0x17:
                    _Registers.RL(ref _Registers.A);
                    return 8;
                case 0x10:
                    _Registers.RL(ref _Registers.B);
                    return 8;
                case 0x11:
                    _Registers.RL(ref _Registers.C);
                    return 8;
                case 0x12:
                    _Registers.RL(ref _Registers.D);
                    return 8;
                case 0x13:
                    _Registers.RL(ref _Registers.E);
                    return 8;
                case 0x14:
                    _Registers.RL(ref _Registers.H);
                    return 8;
                case 0x15:
                    _Registers.RL(ref _Registers.L);
                    return 8;
                case 0x16:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        bool carryBefore = _Registers.CheckFlag(Registers.Flags.C);
                        bool carryAfter = (value & 0x80) != 0;
                        value = (byte)(value << 1);
                        if (carryBefore) value |= 0x01;
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.Z, value == 0);
                        _Registers.UpdateFlag(Registers.Flags.C, carryAfter);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //RR
                case 0x1F:
                    _Registers.RR(ref _Registers.A);
                    return 8;
                case 0x18:
                    _Registers.RR(ref _Registers.B);
                    return 8;
                case 0x19:
                    _Registers.RR(ref _Registers.C);
                    return 8;
                case 0x1A:
                    _Registers.RR(ref _Registers.D);
                    return 8;
                case 0x1B:
                    _Registers.RR(ref _Registers.E);
                    return 8;
                case 0x1C:
                    _Registers.RR(ref _Registers.H);
                    return 8;
                case 0x1D:
                    _Registers.RR(ref _Registers.L);
                    return 8;
                case 0x1E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        bool carryBefore = _Registers.CheckFlag(Registers.Flags.C);
                        bool carryAfter = (value & 0x01) != 0;
                        value = (byte)(value >> 1);
                        if (carryBefore) value |= 0x80;
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.Z, value == 0);
                        _Registers.UpdateFlag(Registers.Flags.C, carryAfter);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //SET
                case 0xC7:
                    _Registers.A = (byte)(_Registers.A | 0x01);
                    return 8;
                case 0xC0:
                    _Registers.B = (byte)(_Registers.B | 0x01);
                    return 8;
                case 0xC1:
                    _Registers.C = (byte)(_Registers.C | 0x01);
                    return 8;
                case 0xC2:
                    _Registers.D = (byte)(_Registers.D | 0x01);
                    return 8;
                case 0xC3:
                    _Registers.E = (byte)(_Registers.E | 0x01);
                    return 8;
                case 0xC4:
                    _Registers.H = (byte)(_Registers.H | 0x01);
                    return 8;
                case 0xC5:
                    _Registers.L = (byte)(_Registers.L | 0x01);
                    return 8;
                case 0xC6:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x01);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xCF:
                    _Registers.A = (byte)(_Registers.A | 0x02);
                    return 8;
                case 0xC8:
                    _Registers.B = (byte)(_Registers.B | 0x02);
                    return 8;
                case 0xC9:
                    _Registers.C = (byte)(_Registers.C | 0x02);
                    return 8;
                case 0xCA:
                    _Registers.D = (byte)(_Registers.D | 0x02);
                    return 8;
                case 0xCB:
                    _Registers.E = (byte)(_Registers.E | 0x02);
                    return 8;
                case 0xCC:
                    _Registers.H = (byte)(_Registers.H | 0x02);
                    return 8;
                case 0xCD:
                    _Registers.L = (byte)(_Registers.L | 0x02);
                    return 8;
                case 0xCE:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x02);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xD7:
                    _Registers.A = (byte)(_Registers.A | 0x04);
                    return 8;
                case 0xD0:
                    _Registers.B = (byte)(_Registers.B | 0x04);
                    return 8;
                case 0xD1:
                    _Registers.C = (byte)(_Registers.C | 0x04);
                    return 8;
                case 0xD2:
                    _Registers.D = (byte)(_Registers.D | 0x04);
                    return 8;
                case 0xD3:
                    _Registers.E = (byte)(_Registers.E | 0x04);
                    return 8;
                case 0xD4:
                    _Registers.H = (byte)(_Registers.H | 0x04);
                    return 8;
                case 0xD5:
                    _Registers.L = (byte)(_Registers.L | 0x04);
                    return 8;
                case 0xD6:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x04);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xDF:
                    _Registers.A = (byte)(_Registers.A | 0x08);
                    return 8;
                case 0xD8:
                    _Registers.B = (byte)(_Registers.B | 0x08);
                    return 8;
                case 0xD9:
                    _Registers.C = (byte)(_Registers.C | 0x08);
                    return 8;
                case 0xDA:
                    _Registers.D = (byte)(_Registers.D | 0x08);
                    return 8;
                case 0xDB:
                    _Registers.E = (byte)(_Registers.E | 0x08);
                    return 8;
                case 0xDC:
                    _Registers.H = (byte)(_Registers.H | 0x08);
                    return 8;
                case 0xDD:
                    _Registers.L = (byte)(_Registers.L | 0x08);
                    return 8;
                case 0xDE:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x08);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xE7:
                    _Registers.A = (byte)(_Registers.A | 0x10);
                    return 8;
                case 0xE0:
                    _Registers.B = (byte)(_Registers.B | 0x10);
                    return 8;
                case 0xE1:
                    _Registers.C = (byte)(_Registers.C | 0x10);
                    return 8;
                case 0xE2:
                    _Registers.D = (byte)(_Registers.D | 0x10);
                    return 8;
                case 0xE3:
                    _Registers.E = (byte)(_Registers.E | 0x10);
                    return 8;
                case 0xE4:
                    _Registers.H = (byte)(_Registers.H | 0x10);
                    return 8;
                case 0xE5:
                    _Registers.L = (byte)(_Registers.L | 0x10);
                    return 8;
                case 0xE6:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x10);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xEF:
                    _Registers.A = (byte)(_Registers.A | 0x20);
                    return 8;
                case 0xE8:
                    _Registers.B = (byte)(_Registers.B | 0x20);
                    return 8;
                case 0xE9:
                    _Registers.C = (byte)(_Registers.C | 0x20);
                    return 8;
                case 0xEA:
                    _Registers.D = (byte)(_Registers.D | 0x20);
                    return 8;
                case 0xEB:
                    _Registers.E = (byte)(_Registers.E | 0x20);
                    return 8;
                case 0xEC:
                    _Registers.H = (byte)(_Registers.H | 0x20);
                    return 8;
                case 0xED:
                    _Registers.L = (byte)(_Registers.L | 0x20);
                    return 8;
                case 0xEE:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x20);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xF7:
                    _Registers.A = (byte)(_Registers.A | 0x40);
                    return 8;
                case 0xF0:
                    _Registers.B = (byte)(_Registers.B | 0x40);
                    return 8;
                case 0xF1:
                    _Registers.C = (byte)(_Registers.C | 0x40);
                    return 8;
                case 0xF2:
                    _Registers.D = (byte)(_Registers.D | 0x40);
                    return 8;
                case 0xF3:
                    _Registers.E = (byte)(_Registers.E | 0x40);
                    return 8;
                case 0xF4:
                    _Registers.H = (byte)(_Registers.H | 0x40);
                    return 8;
                case 0xF5:
                    _Registers.L = (byte)(_Registers.L | 0x40);
                    return 8;
                case 0xF6:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x40);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xFF:
                    _Registers.A = (byte)(_Registers.A | 0x80);
                    return 8;
                case 0xF8:
                    _Registers.B = (byte)(_Registers.B | 0x80);
                    return 8;
                case 0xF9:
                    _Registers.C = (byte)(_Registers.C | 0x80);
                    return 8;
                case 0xFA:
                    _Registers.D = (byte)(_Registers.D | 0x80);
                    return 8;
                case 0xFB:
                    _Registers.E = (byte)(_Registers.E | 0x80);
                    return 8;
                case 0xFC:
                    _Registers.H = (byte)(_Registers.H | 0x80);
                    return 8;
                case 0xFD:
                    _Registers.L = (byte)(_Registers.L | 0x80);
                    return 8;
                case 0xFE:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value | 0x80);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //RES
                case 0x87:
                    _Registers.A = (byte)(_Registers.A & ~0x01);
                    return 8;
                case 0X80:
                    _Registers.B = (byte)(_Registers.B & ~0x01);
                    return 8;
                case 0x81:
                    _Registers.C = (byte)(_Registers.C & ~0x01);
                    return 8;
                case 0x82:
                    _Registers.D = (byte)(_Registers.D & ~0x01);
                    return 8;
                case 0x83:
                    _Registers.E = (byte)(_Registers.E & ~0x01);
                    return 8;
                case 0x84:
                    _Registers.H = (byte)(_Registers.H & ~0x01);
                    return 8;
                case 0x85:
                    _Registers.L = (byte)(_Registers.L & ~0x01);
                    return 8;
                case 0x86:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x01);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0x8F:
                    _Registers.A = (byte)(_Registers.A & ~0x02);
                    return 8;
                case 0X88:
                    _Registers.B = (byte)(_Registers.B & ~0x02);
                    return 8;
                case 0x89:
                    _Registers.C = (byte)(_Registers.C & ~0x02);
                    return 8;
                case 0x8A:
                    _Registers.D = (byte)(_Registers.D & ~0x02);
                    return 8;
                case 0x8B:
                    _Registers.E = (byte)(_Registers.E & ~0x02);
                    return 8;
                case 0x8C:
                    _Registers.H = (byte)(_Registers.H & ~0x02);
                    return 8;
                case 0x8D:
                    _Registers.L = (byte)(_Registers.L & ~0x02);
                    return 8;
                case 0x8E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x02);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0x97:
                    _Registers.A = (byte)(_Registers.A & ~0x04);
                    return 8;
                case 0X90:
                    _Registers.B = (byte)(_Registers.B & ~0x04);
                    return 8;
                case 0x91:
                    _Registers.C = (byte)(_Registers.C & ~0x04);
                    return 8;
                case 0x92:
                    _Registers.D = (byte)(_Registers.D & ~0x04);
                    return 8;
                case 0x93:
                    _Registers.E = (byte)(_Registers.E & ~0x04);
                    return 8;
                case 0x94:
                    _Registers.H = (byte)(_Registers.H & ~0x04);
                    return 8;
                case 0x95:
                    _Registers.L = (byte)(_Registers.L & ~0x04);
                    return 8;
                case 0x96:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x04);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0x9F:
                    _Registers.A = (byte)(_Registers.A & ~0x08);
                    return 8;
                case 0X98:
                    _Registers.B = (byte)(_Registers.B & ~0x08);
                    return 8;
                case 0x99:
                    _Registers.C = (byte)(_Registers.C & ~0x08);
                    return 8;
                case 0x9A:
                    _Registers.D = (byte)(_Registers.D & ~0x08);
                    return 8;
                case 0x9B:
                    _Registers.E = (byte)(_Registers.E & ~0x08);
                    return 8;
                case 0x9C:
                    _Registers.H = (byte)(_Registers.H & ~0x08);
                    return 8;
                case 0x9D:
                    _Registers.L = (byte)(_Registers.L & ~0x08);
                    return 8;
                case 0x9E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x08);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xA7:
                    _Registers.A = (byte)(_Registers.A & ~0x10);
                    return 8;
                case 0xA0:
                    _Registers.B = (byte)(_Registers.B & ~0x10);
                    return 8;
                case 0xA1:
                    _Registers.C = (byte)(_Registers.C & ~0x10);
                    return 8;
                case 0xA2:
                    _Registers.D = (byte)(_Registers.D & ~0x10);
                    return 8;
                case 0xA3:
                    _Registers.E = (byte)(_Registers.E & ~0x10);
                    return 8;
                case 0xA4:
                    _Registers.H = (byte)(_Registers.H & ~0x10);
                    return 8;
                case 0xA5:
                    _Registers.L = (byte)(_Registers.L & ~0x10);
                    return 8;
                case 0xA6:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x10);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xAF:
                    _Registers.A = (byte)(_Registers.A & ~0x20);
                    return 8;
                case 0xA8:
                    _Registers.B = (byte)(_Registers.B & ~0x20);
                    return 8;
                case 0xA9:
                    _Registers.C = (byte)(_Registers.C & ~0x20);
                    return 8;
                case 0xAA:
                    _Registers.D = (byte)(_Registers.D & ~0x20);
                    return 8;
                case 0xAB:
                    _Registers.E = (byte)(_Registers.E & ~0x20);
                    return 8;
                case 0xAC:
                    _Registers.H = (byte)(_Registers.H & ~0x20);
                    return 8;
                case 0xAD:
                    _Registers.L = (byte)(_Registers.L & ~0x20);
                    return 8;
                case 0xAE:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x20);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xB7:
                    _Registers.A = (byte)(_Registers.A & ~0x40);
                    return 8;
                case 0xB0:
                    _Registers.B = (byte)(_Registers.B & ~0x40);
                    return 8;
                case 0xB1:
                    _Registers.C = (byte)(_Registers.C & ~0x40);
                    return 8;
                case 0xB2:
                    _Registers.D = (byte)(_Registers.D & ~0x40);
                    return 8;
                case 0xB3:
                    _Registers.E = (byte)(_Registers.E & ~0x40);
                    return 8;
                case 0xB4:
                    _Registers.H = (byte)(_Registers.H & ~0x40);
                    return 8;
                case 0xB5:
                    _Registers.L = (byte)(_Registers.L & ~0x40);
                    return 8;
                case 0xB6:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x40);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                case 0xBF:
                    _Registers.A = (byte)(_Registers.A & ~0x80);
                    return 8;
                case 0xB8:
                    _Registers.B = (byte)(_Registers.B & ~0x80);
                    return 8;
                case 0xB9:
                    _Registers.C = (byte)(_Registers.C & ~0x80);
                    return 8;
                case 0xBA:
                    _Registers.D = (byte)(_Registers.D & ~0x80);
                    return 8;
                case 0xBB:
                    _Registers.E = (byte)(_Registers.E & ~0x80);
                    return 8;
                case 0xBC:
                    _Registers.H = (byte)(_Registers.H & ~0x80);
                    return 8;
                case 0xBD:
                    _Registers.L = (byte)(_Registers.L & ~0x80);
                    return 8;
                case 0xBE:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)(value & ~0x80);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //SLA
                case 0x27:
                    _Registers.SLA(ref _Registers.A);
                    return 8;
                case 0x20:
                    _Registers.SLA(ref _Registers.B);
                    return 8;
                case 0x21:
                    _Registers.SLA(ref _Registers.C);
                    return 8;
                case 0x22:
                    _Registers.SLA(ref _Registers.D);
                    return 8;
                case 0x23:
                    _Registers.SLA(ref _Registers.E);
                    return 8;
                case 0x24:
                    _Registers.SLA(ref _Registers.H);
                    return 8;
                case 0x25:
                    _Registers.SLA(ref _Registers.L);
                    return 8;
                case 0x26:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.SLA(ref value);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //SRA
                case 0x2F:
                    _Registers.SRA(ref _Registers.A);
                    return 8;
                case 0x28:
                    _Registers.SRA(ref _Registers.B);
                    return 8;
                case 0x29:
                    _Registers.SRA(ref _Registers.C);
                    return 8;
                case 0x2A:
                    _Registers.SRA(ref _Registers.D);
                    return 8;
                case 0x2B:
                    _Registers.SRA(ref _Registers.E);
                    return 8;
                case 0x2C:
                    _Registers.SRA(ref _Registers.H);
                    return 8;
                case 0x2D:
                    _Registers.SRA(ref _Registers.L);
                    return 8;
                case 0x2E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.SRA(ref value);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //SWAP
                case 0x37:
                    _Registers.Swap(ref _Registers.A);
                    return 8;
                case 0x30:
                    _Registers.Swap(ref _Registers.B);
                    return 8;
                case 0x31:
                    _Registers.Swap(ref _Registers.C);
                    return 8;
                case 0x32:
                    _Registers.Swap(ref _Registers.D);
                    return 8;
                case 0x33:
                    _Registers.Swap(ref _Registers.E);
                    return 8;
                case 0x34:
                    _Registers.Swap(ref _Registers.H);
                    return 8;
                case 0x35:
                    _Registers.Swap(ref _Registers.L);
                    return 8;
                case 0x36:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        value = (byte)((value >> 4) | (value << 4));
                        _Registers.ClearF();
                        _Registers.UpdateFlag(Registers.Flags.Z, value == 0);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //SRL
                case 0x3F:
                    _Registers.SRL(ref _Registers.A);
                    return 8;
                case 0x38:
                    _Registers.SRL(ref _Registers.B);
                    return 8;
                case 0x39:
                    _Registers.SRL(ref _Registers.C);
                    return 8;
                case 0x3A:
                    _Registers.SRL(ref _Registers.D);
                    return 8;
                case 0x3B:
                    _Registers.SRL(ref _Registers.E);
                    return 8;
                case 0x3C:
                    _Registers.SRL(ref _Registers.H);
                    return 8;
                case 0x3D:
                    _Registers.SRL(ref _Registers.L);
                    return 8;
                case 0x3E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.SRL(ref value);
                        Write8Bit(_Registers.HL, value);
                        return 16;
                    }
                //BIT 0
                case 0x47:
                    _Registers.BIT(_Registers.A, 0);
                    return 8;
                case 0x40:
                    _Registers.BIT(_Registers.B, 0);
                    return 8;
                case 0x41:
                    _Registers.BIT(_Registers.C, 0);
                    return 8;
                case 0x42:
                    _Registers.BIT(_Registers.D, 0);
                    return 8;
                case 0x43:
                    _Registers.BIT(_Registers.E, 0);
                    return 8;
                case 0x44:
                    _Registers.BIT(_Registers.H, 0);
                    return 8;
                case 0x45:
                    _Registers.BIT(_Registers.L, 0);
                    return 8;
                case 0x46:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 0);
                        return 12;
                    }
                case 0x4F:
                    _Registers.BIT(_Registers.A, 1);
                    return 8;
                case 0x48:
                    _Registers.BIT(_Registers.B, 1);
                    return 8;
                case 0x49:
                    _Registers.BIT(_Registers.C, 1);
                    return 8;
                case 0x4A:
                    _Registers.BIT(_Registers.D, 1);
                    return 8;
                case 0x4B:
                    _Registers.BIT(_Registers.E, 1);
                    return 8;
                case 0x4C:
                    _Registers.BIT(_Registers.H, 1);
                    return 8;
                case 0x4D:
                    _Registers.BIT(_Registers.L, 1);
                    return 8;
                case 0x4E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 1);
                        return 12;
                    }
                case 0x57:
                    _Registers.BIT(_Registers.A, 2);
                    return 8;
                case 0x50:
                    _Registers.BIT(_Registers.B, 2);
                    return 8;
                case 0x51:
                    _Registers.BIT(_Registers.C, 2);
                    return 8;
                case 0x52:
                    _Registers.BIT(_Registers.D, 2);
                    return 8;
                case 0x53:
                    _Registers.BIT(_Registers.E, 2);
                    return 8;
                case 0x54:
                    _Registers.BIT(_Registers.H, 2);
                    return 8;
                case 0x55:
                    _Registers.BIT(_Registers.L, 2);
                    return 8;
                case 0x56:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 2);
                        return 12;
                    }
                case 0x5F:
                    _Registers.BIT(_Registers.A, 3);
                    return 8;
                case 0x58:
                    _Registers.BIT(_Registers.B, 3);
                    return 8;
                case 0x59:
                    _Registers.BIT(_Registers.C, 3);
                    return 8;
                case 0x5A:
                    _Registers.BIT(_Registers.D, 3);
                    return 8;
                case 0x5B:
                    _Registers.BIT(_Registers.E, 3);
                    return 8;
                case 0x5C:
                    _Registers.BIT(_Registers.H, 3);
                    return 8;
                case 0x5D:
                    _Registers.BIT(_Registers.L, 3);
                    return 8;
                case 0x5E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 3);
                        return 12;
                    }
                case 0x67:
                    _Registers.BIT(_Registers.A, 4);
                    return 8;
                case 0x60:
                    _Registers.BIT(_Registers.B, 4);
                    return 8;
                case 0x61:
                    _Registers.BIT(_Registers.C, 4);
                    return 8;
                case 0x62:
                    _Registers.BIT(_Registers.D, 4);
                    return 8;
                case 0x63:
                    _Registers.BIT(_Registers.E, 4);
                    return 8;
                case 0x64:
                    _Registers.BIT(_Registers.H, 4);
                    return 8;
                case 0x65:
                    _Registers.BIT(_Registers.L, 4);
                    return 8;
                case 0x66:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 4);
                        return 12;
                    }
                case 0x6F:
                    _Registers.BIT(_Registers.A, 5);
                    return 8;
                case 0x68:
                    _Registers.BIT(_Registers.B, 5);
                    return 8;
                case 0x69:
                    _Registers.BIT(_Registers.C, 5);
                    return 8;
                case 0x6A:
                    _Registers.BIT(_Registers.D, 5);
                    return 8;
                case 0x6B:
                    _Registers.BIT(_Registers.E, 5);
                    return 8;
                case 0x6C:
                    _Registers.BIT(_Registers.H, 5);
                    return 8;
                case 0x6D:
                    _Registers.BIT(_Registers.L, 5);
                    return 8;
                case 0x6E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 5);
                        return 12;
                    }
                case 0x77:
                    _Registers.BIT(_Registers.A, 6);
                    return 8;
                case 0x70:
                    _Registers.BIT(_Registers.B, 6);
                    return 8;
                case 0x71:
                    _Registers.BIT(_Registers.C, 6);
                    return 8;
                case 0x72:
                    _Registers.BIT(_Registers.D, 6);
                    return 8;
                case 0x73:
                    _Registers.BIT(_Registers.E, 6);
                    return 8;
                case 0x74:
                    _Registers.BIT(_Registers.H, 6);
                    return 8;
                case 0x75:
                    _Registers.BIT(_Registers.L, 6);
                    return 8;
                case 0x76:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 6);
                        return 12;
                    }
                case 0x7F:
                    _Registers.BIT(_Registers.A, 7);
                    return 8;
                case 0x78:
                    _Registers.BIT(_Registers.B, 7);
                    return 8;
                case 0x79:
                    _Registers.BIT(_Registers.C, 7);
                    return 8;
                case 0x7A:
                    _Registers.BIT(_Registers.D, 7);
                    return 8;
                case 0x7B:
                    _Registers.BIT(_Registers.E, 7);
                    return 8;
                case 0x7C:
                    _Registers.BIT(_Registers.H, 7);
                    return 8;
                case 0x7D:
                    _Registers.BIT(_Registers.L, 7);
                    return 8;
                case 0x7E:
                    {
                        byte value = Read8Bit(_Registers.HL);
                        _Registers.BIT(value, 7);
                        return 12;
                    }
                default:
                    Logger.LogEnable = true;
                    Logger.Write("CB COMMAND");
                    Logger.Write($"Unknown opcode: {opCode:X2} at PC:{(_Registers.PC - 1):X4}");
                    Logger.Write(
                        $"A:{_Registers.A:X2} F:{_Registers.F:X2} " +
                        $"B:{_Registers.B:X2} C:{_Registers.C:X2} " +
                        $"D:{_Registers.D:X2} E:{_Registers.E:X2} " +
                        $"H:{_Registers.H:X2} L:{_Registers.L:X2} " +
                        $"SP:{_Registers.SP:X4}"
                    );
                    Logger.LogEnable = false;
                    return 0;
            }
        }

        public void Save(BinaryWriter stateWriter)
        {
            //REGISTERS
            stateWriter.Write(_Registers.A);
            stateWriter.Write(_Registers.B);
            stateWriter.Write(_Registers.C);
            stateWriter.Write(_Registers.D);
            stateWriter.Write(_Registers.E);
            stateWriter.Write(_Registers.F);
            stateWriter.Write(_Registers.H);
            stateWriter.Write(_Registers.L);

            //PC AND STACK POINTER
            stateWriter.Write(_Registers.PC);
            stateWriter.Write(_Registers.SP);

            //INTERRUPTS
            stateWriter.Write(IME);
            stateWriter.Write(imeEnableDelay);
            stateWriter.Write(_Registers.HALT);
            stateWriter.Write(haltBug);
        }
        public void Load(BinaryReader stateReader)
        {
            //REGISTERS
            _Registers.A = stateReader.ReadByte();
            _Registers.B = stateReader.ReadByte();
            _Registers.C = stateReader.ReadByte();
            _Registers.D = stateReader.ReadByte();
            _Registers.E = stateReader.ReadByte();
            _Registers.F = stateReader.ReadByte();
            _Registers.H = stateReader.ReadByte();
            _Registers.L = stateReader.ReadByte();

            //PC AND STACK POINTER
            _Registers.PC = stateReader.ReadUInt16();
            _Registers.SP = stateReader.ReadUInt16();

            //INTERRUPTS
            IME = stateReader.ReadBoolean();
            imeEnableDelay = stateReader.ReadInt32();
            _Registers.HALT = stateReader.ReadBoolean();
            haltBug = stateReader.ReadBoolean();
        }
    }
}
