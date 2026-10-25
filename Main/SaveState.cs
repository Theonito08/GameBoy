using System.IO;

namespace GameBoyEmu.Main
{
    public interface ISaveState
    {
        void Save(BinaryWriter stateWriter);
        void Load(BinaryReader stateReader);
    }
}
