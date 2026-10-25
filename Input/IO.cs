using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameBoyEmu.Input
{
    public interface IJoypad
    {
        bool IsRightPressed();
        bool IsLeftPressed();
        bool IsUpPressed();
        bool IsDownPressed();
        bool IsAPressed();
        bool IsBPressed();
        bool IsSelectPressed();
        bool IsStartPressed();
    }
}
