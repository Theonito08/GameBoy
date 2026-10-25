namespace GameBoyEmu.Processor
{
    public class OpcodeQueue
    {
        private string[] opcodeStore = new string[500];
        private int rearPointer = 0;
        private int frontPointer = 0;
        private int count = 0;


        public void Enqueue(string opcodeInfo)
        {
            opcodeStore[rearPointer] = opcodeInfo;
            rearPointer = (rearPointer + 1) % opcodeStore.Length;

            if (count < opcodeStore.Length)
            {
                count++;
            }
            else
            {
                frontPointer = (frontPointer + 1) % opcodeStore.Length;
            }
        }

        public string[] GetOpcodeInfo()
        {
            string[] result = new string[count];

            for (int i = 0; i < count; i++)
            {
                result[i] = opcodeStore[(frontPointer + i) % opcodeStore.Length];
            }

            return result;
        }
    }
}
