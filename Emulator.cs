using GameBoyEmu.Main;
using Gtk;
using System;
using System.IO;

namespace GameBoyEmu
{
    internal static class Emulator
    {

        static void Main(string[] args)
        {
            string baseDir = AppContext.BaseDirectory;

            string schemaDir = Path.Combine(baseDir, "share", "glib-2.0", "schemas");
            string shareDir = Path.Combine(baseDir, "share");

            Environment.SetEnvironmentVariable("GSETTINGS_SCHEMA_DIR", schemaDir);
            Environment.SetEnvironmentVariable("XDG_DATA_DIRS", shareDir);

            Application.Init();

            WindowScreen win = new WindowScreen();
            win.ShowAll();

            Application.Run();
        }
    }
}

