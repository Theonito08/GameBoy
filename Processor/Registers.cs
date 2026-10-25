using GameBoyEmu.Debugger;
namespace GameBoyEmu.Processor
{
    public class Registers
    {
        public bool HALT;
        public byte A, B, C, D, E, F, H, L;
        public ushort PC, SP;

        public ushort AF
        {
            get
            {
                return (ushort)((A << 8) | F);
            }
            set
            {
                A = (byte)(value >> 8);
                F = (byte)(value & 0xF0);
            }
        }

        public ushort BC
        {
            get
            {
                return (ushort)((B << 8) | C);
            }
            set
            {
                B = (byte)(value >> 8);
                C = (byte)(value & 0xFF);
            }
        }

        public ushort DE
        {
            get
            {
                return (ushort)((D << 8) | E);
            }
            set
            {
                D = (byte)(value >> 8);
                E = (byte)(value & 0xFF);
            }
        }

        public ushort HL
        {
            get
            {
                return (ushort)((H << 8) | L);
            }
            set
            {
                H = (byte)(value >> 8);
                L = (byte)(value & 0xFF);
            }
        }

        public Registers()
        {
            ResetRegisters();
        }

        public enum Flags
        {
            Z,
            N,
            H,
            C
        }
        public void ResetRegisters()
        {
            A = 0x01;
            F = 0xB0;
            B = 0x00;
            C = 0x13;
            D = 0x00;
            E = 0xD8;
            H = 0x01;
            L = 0x4D;

            SP = 0xFFFE;
            PC = 0x0100;

            AF = 0x01B0;
            BC = 0x0013;
            DE = 0x00D8;
            HL = 0x014D;

            HALT = false;
        }

        public byte GetFlagPosition(Flags flagName)
        {
            switch (flagName)
            {
                case Flags.Z:
                    return 0x80;
                case Flags.N:
                    return 0x40;
                case Flags.H:
                    return 0x20;
                case Flags.C:
                    return 0x10;
                default:
                    return 0x00;
            }
        }

        public void UpdateFlag(Flags flagName, bool condition)
        {
            byte flagPosition = GetFlagPosition(flagName);
            if (condition) F = (byte)(F | flagPosition);
            else F = (byte)(F & ~flagPosition);
        }

        public bool CheckFlag(Flags flagName)
        {
            byte flagPosition = GetFlagPosition(flagName);
            return (F & flagPosition) != 0;
        }

        public void ClearF()
        {
            F = 0x00;
        }
        public void Addition(byte operand)
        {
            int result = (A + operand);
            UpdateFlag(Flags.Z, (byte)result == 0);
            UpdateFlag(Flags.N, false);
            UpdateFlag(Flags.H, (A & 0x0F) + (operand & 0x0F) > 0x0F);
            UpdateFlag(Flags.C, result > 0xFF);
            A = (byte)(result);
        }

        public void WordAddition(ushort registerPair)
        {
            int result = (HL + registerPair);
            UpdateFlag(Flags.N, false);
            UpdateFlag(Flags.H, (HL & 0x0FFF) + (registerPair & 0x0FFF) > 0x0FFF);
            UpdateFlag(Flags.C, result > 0xFFFF);
            HL = (ushort)result;
        }

        public void Swap(ref byte operand)
        {
            operand = (byte)((operand >> 4) | (operand << 4));
            ClearF();
            UpdateFlag(Flags.Z, operand == 0);
        }

        public void SRL(ref byte operand)
        {
            bool bit0 = (operand & 0x01) != 0;
            operand = (byte)(operand >> 1);
            ClearF();
            UpdateFlag(Flags.Z, operand == 0);
            UpdateFlag(Flags.C, bit0);
        }

        public void SLA(ref byte operand)
        {
            bool bit7 = (operand & 0x80) != 0;
            operand = (byte)(operand << 1);
            ClearF();
            UpdateFlag(Flags.Z, operand == 0);
            UpdateFlag(Flags.C, bit7);
        }

        public void SRA(ref byte operand)
        {
            bool bit7 = (operand & 0x80) != 0;
            bool bit0 = (operand & 0x01) != 0;
            operand = (byte)(operand >> 1);
            ClearF();
            if (bit7) operand = (byte)(operand | 0x80);
            UpdateFlag(Flags.Z, operand == 0);
            UpdateFlag(Flags.C, bit0);
        }

        public void DAA()
        {
            int registerA = A;
            bool NFlag = CheckFlag(Flags.N);
            bool HFlag = CheckFlag(Flags.H);
            bool CFlag = CheckFlag(Flags.C);
            bool carry = CFlag;
            int correction = 0;
            if (HFlag || (!NFlag && (registerA & 0x0F) > 0x09)) correction |= 0x06;
            if (CFlag || (!NFlag && (registerA > 0x99)))
            {
                correction |= 0x60;
                carry = true;
            }
            if (NFlag) registerA -= correction;
            else registerA += correction;
            A = (byte)(registerA);
            UpdateFlag(Flags.H, false);
            UpdateFlag(Flags.Z, A == 0);
            UpdateFlag(Flags.C, carry);
        }

        public void RLC(ref byte operand)
        {
            bool bit7 = (operand & 0x80) != 0;
            operand = (byte)((operand << 1) | (bit7 ? 0x01 : 0x00));
            ClearF();
            UpdateFlag(Flags.Z, operand == 0);
            UpdateFlag(Flags.C, bit7);
        }

        public void RRC(ref byte operand)
        {
            bool bit0 = (operand & 0x01) != 0;
            operand = (byte)((operand >> 1) | (bit0 ? 0x80 : 0x00));
            ClearF();
            UpdateFlag(Flags.Z, operand == 0);
            UpdateFlag(Flags.C, bit0);
        }

        public void RL(ref byte operand)
        {
            bool carryBefore = CheckFlag(Flags.C);
            bool carryAfter = (operand & 0x80) != 0;
            operand = (byte)(operand << 1);
            if (carryBefore) operand |= 0x01;
            ClearF();
            UpdateFlag(Flags.Z, operand == 0);
            UpdateFlag(Flags.C, carryAfter);
        }

        public void RR(ref byte operand)
        {
            bool carryBefore = CheckFlag(Flags.C);
            bool carryAfter = (operand & 0x01) != 0;
            operand = (byte)(operand >> 1);
            if (carryBefore) operand |= 0x80;
            ClearF();
            UpdateFlag(Flags.Z, operand == 0);
            UpdateFlag(Flags.C, carryAfter);
        }

        public void AdditionWithCarry(byte operand)
        {
            int carry = CheckFlag(Flags.C) ? 1 : 0;
            int result = (A + operand + carry);
            UpdateFlag(Flags.Z, (byte)result == 0);
            UpdateFlag(Flags.N, false);
            UpdateFlag(Flags.H, ((A & 0x0F) + (operand & 0x0F) + carry) > 0x0F);
            UpdateFlag(Flags.C, result > 0xFF);
            A = (byte)result;
        }

        public void Subtraction(byte operand)
        {
            int result = A - operand;
            UpdateFlag(Flags.Z, (byte)result == 0);
            UpdateFlag(Flags.N, true);
            UpdateFlag(Flags.H, (A & 0x0F) < (operand & 0x0F));
            UpdateFlag(Flags.C, A < operand);
            A = (byte)result;
        }

        public void SubtractionWithCarry(byte operand)
        {
            int carry = CheckFlag(Flags.C) ? 1 : 0;
            int result = (A - operand - carry);
            UpdateFlag(Flags.Z, (byte)result == 0);
            UpdateFlag(Flags.N, true);
            UpdateFlag(Flags.H, (A & 0x0F) < ((operand & 0x0F) + carry));
            UpdateFlag(Flags.C, A < operand + carry);
            A = (byte)result;
        }

        public void Increment(ref byte register)
        {
            int result = register + 1;
            UpdateFlag(Flags.Z, (byte)result == 0);
            UpdateFlag(Flags.N, false);
            UpdateFlag(Flags.H, (register & 0x0F) + 1 > 0x0F);
            register = (byte)result;
        }

        public void Decrement(ref byte register)
        {
            int result = register - 1;
            UpdateFlag(Flags.Z, (byte)result == 0);
            UpdateFlag(Flags.N, true);
            UpdateFlag(Flags.H, (register & 0x0F) == 0);
            register = (byte)result;
        }

        public void Comp(byte operand)
        {
            int result = A - operand;
            UpdateFlag(Flags.Z, result == 0);
            UpdateFlag(Flags.N, true);
            UpdateFlag(Flags.H, (A & 0x0F) < (operand & 0x0F));
            UpdateFlag(Flags.C, A < operand);
        }

        public void BitwiseAND(byte operand)
        {
            A = (byte)(A & operand);
            UpdateFlag(Flags.Z, A == 0);
            UpdateFlag(Flags.N, false);
            UpdateFlag(Flags.H, true);
            UpdateFlag(Flags.C, false);
        }

        public void BitwiseXOR(byte operand)
        {
            A = (byte)(A ^ operand);
            ClearF();
            UpdateFlag(Flags.Z, A == 0);
        }

        public void BitwiseOR(byte OpOrRegister)
        {
            A = (byte)(A | OpOrRegister);
            ClearF();
            UpdateFlag(Flags.Z, A == 0);
        }

        public void BIT(byte operand, int bit)
        {
            UpdateFlag(Flags.Z, (operand & (1 << bit)) == 0);
            UpdateFlag(Flags.N, false);
            UpdateFlag(Flags.H, true);
        }
    }
}