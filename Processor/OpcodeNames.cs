using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameBoyEmu.Processor
{
    public class OpcodeNames
    {
        private string[] opNames;
        public OpcodeNames()
        {
            opNames = File.ReadAllLines("Processor/opcodes.txt");
        }
        public string GetOpCodeName(byte index)
        {
            /*if(index == 0x00)
            {
                return opNames[index];
            }*/
            return opNames[index];
           // return $"{opNames[0x00]} {opNames[0x01]}";
        }
    }
}
