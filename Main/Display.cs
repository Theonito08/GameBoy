using GameBoyEmu.Audio;
using GameBoyEmu.Cartrige;
using GameBoyEmu.Debugger;
using GameBoyEmu.Graphics;
using GameBoyEmu.Input;
using GameBoyEmu.Memory;
using GameBoyEmu.Processor;
using GameBoyEmu.Visual;
using Gdk;
using Gtk;
using System;
using System.Diagnostics;
using System.IO;
namespace GameBoyEmu.Main
{
    internal class WindowScreen : Gtk.Window, IJoypad
    {

        private DebuggerWindow debuggerWindow;
        VBox layout = new VBox(false, 0);
        MenuBar menuBar = new MenuBar();


        MenuItem openDebugger = new MenuItem("Open Debugger");
        MenuItem fileMenuItem = new MenuItem("File");
        MenuItem loadRomItem = new MenuItem("Load ROM");
        MenuItem emulatorMenuItem = new MenuItem("Emulator");
        MenuItem debuggerMenuItem = new MenuItem("Debugger");
        MenuItem logFileItem = new MenuItem("Check Log File");
        MenuItem SaveStateItem = new MenuItem("Save State");
        MenuItem LoadStateItem = new MenuItem("Load State");
        CheckMenuItem debugModeItem = new CheckMenuItem("Enable Debug Mode");
        CheckMenuItem pauseItem = new CheckMenuItem("Pause");

        Menu fileMenu = new Menu();
        Menu emulatorMenu = new Menu();
        Menu debuggerMenu = new Menu();

        private const int WIDTH = 160;
        private const int HEIGHT = 144;
        private const int SCALE = 4;

        private bool rightPressed, leftPressed, upPressed, downPressed;
        private bool aPressed, bPressed, selectPressed, startPressed;

        public bool IsRightPressed() { return rightPressed; }
        public bool IsLeftPressed() { return leftPressed; }
        public bool IsUpPressed() { return upPressed; }
        public bool IsDownPressed() { return downPressed; }
        public bool IsAPressed() { return aPressed; }
        public bool IsBPressed() { return bPressed; }
        public bool IsSelectPressed() { return selectPressed; }
        public bool IsStartPressed() { return startPressed; }

        private MEMORY memory;
        private CPU cpu;
        private GameBoyFrameBuffer frameBuffer;
        private GameBoyDisplay display;
        private GPU gpu;
        private Registers registers;
        private Cartridge cartrige;
        private APU apu;

        private bool paused = true;
        public WindowScreen() : base("Game Boy Emulator")
        {
            SetDefaultSize(WIDTH * SCALE + 40, HEIGHT * SCALE + 80);
            DeleteEvent += WindowDelete;
            Logger.InitLogWriter();
            BuildEmu();
            BuildUI();
            GLib.Timeout.Add(1, EmulatorLoop);
            ShowAll();
        }

        private void WindowDelete(object sender, DeleteEventArgs e)
        {
            Logger.TerminateLogging();
            Application.Quit();
        }


        private void BuildEmu()
        {
            memory = new MEMORY();
            memory.ConnectJoypad(this);
            registers = new Registers();
            if (frameBuffer == null)
            {
                frameBuffer = new GameBoyFrameBuffer();
                display = new GameBoyDisplay(frameBuffer);
            }
            else
            {
                frameBuffer.ClearBuffer();
            }
            PPURenderer renderer = new PPURenderer(memory, frameBuffer);
            gpu = new GPU(memory, renderer);
            cpu = new CPU(registers, memory);
            apu = new APU(memory);
            memory.ConnectAPU(apu);
            cpu.ConnectHardware(gpu, apu);
        }

        private void BuildUI()
        {
            loadRomItem.Activated += OpenRomClicked;
            SaveStateItem.Activated += SaveStateClicked;
            LoadStateItem.Activated += LoadStateClicked;
            fileMenu.Append(loadRomItem);
            fileMenu.Append(SaveStateItem);
            fileMenu.Append(LoadStateItem);



            fileMenuItem.Submenu = fileMenu;


            pauseItem.Active = paused;
            pauseItem.Label = paused ? "✓ Pause" : "Pause";
            pauseItem.Toggled += OnPause;

            emulatorMenu.Append(pauseItem);
            emulatorMenuItem.Submenu = emulatorMenu;

            debugModeItem.Active = Logger.LogEnable;
            debugModeItem.Toggled += delegate
            {
                Logger.LogEnable = debugModeItem.Active;
            };
            logFileItem.Activated += OpenLogFile;
            debuggerMenu.Append(debugModeItem);
            debuggerMenu.Append(logFileItem);
            debuggerMenuItem.Submenu = debuggerMenu;

            openDebugger.Activated += OpenDebugger;
            debuggerMenu.Append(openDebugger);

            menuBar.Append(fileMenuItem);
            menuBar.Append(emulatorMenuItem);
            menuBar.Append(debuggerMenuItem);

            display.widget.SetSizeRequest(WIDTH * SCALE, HEIGHT * SCALE);
            display.widget.CanFocus = true;
            display.widget.AddEvents((int)(EventMask.KeyPressMask | EventMask.KeyReleaseMask | EventMask.ButtonPressMask));

            display.widget.KeyPressEvent += OnKeyDown;
            display.widget.KeyReleaseEvent += OnKeyUp;  

            display.widget.ButtonPressEvent += delegate
            {
                display.widget.GrabFocus();
            };

            layout.PackStart(menuBar, false, false, 0);
            layout.PackStart(display.widget, true, true, 0);
            Add(layout);
        }

        private void OnPause(object sender, EventArgs e)
        {
            Paused(pauseItem.Active);
        }
        private void Paused(bool pressed)
        {
            paused = pressed;
            if (pauseItem.Active != paused)
            {
                pauseItem.Active = paused;
            }
            pauseItem.Label = paused ? "✓ Pause" : "Pause";
        }

        private void OpenRomClicked(object sender, EventArgs e)
        {
            paused = true;

            using (FileChooserDialog PickROM = new FileChooserDialog(
                "Open ROM",
                this,
                FileChooserAction.Open,
                "Cancel", ResponseType.Cancel,
                "Open", ResponseType.Accept))
            {
                FileFilter gbFilter = new FileFilter();
                gbFilter.Name = "Game Boy ROMs";
                gbFilter.AddPattern("*.gb");
                gbFilter.AddPattern("*.gbc");
                PickROM.AddFilter(gbFilter);

                FileFilter allFilter = new FileFilter();
                allFilter.Name = "All files";
                allFilter.AddPattern("*");
                PickROM.AddFilter(allFilter);

                if (PickROM.Run() == (int)ResponseType.Accept)
                {
                    LoadROM(PickROM.Filename);
                }
                else
                {
                    paused = true;
                }
            }
        }

        private void SaveStateClicked(object sender, EventArgs e)
        {
            bool wasPaused = paused;
            paused = true;
            FileChooserDialog SaveStateDialog = new FileChooserDialog(
                "Save State",
                 this,
                 FileChooserAction.Save,
                 "Cancel", ResponseType.Cancel,
                 "Save", ResponseType.Accept
            );
            SaveStateDialog.CurrentName = "save_state.sav";
            if (SaveStateDialog.Run() == (int)ResponseType.Accept)
            {
                SaveState(SaveStateDialog.Filename);
            }
            SaveStateDialog.Destroy();
            paused = wasPaused;
            display.widget.GrabFocus();
        }

        private void LoadStateClicked(object sender, EventArgs e)
        {
            bool wasPaused = paused;
            paused = true;
            FileChooserDialog LoadStateDialog = new FileChooserDialog
            (
                "Load state",
                this,
                FileChooserAction.Open,
                "Cancel", ResponseType.Cancel,
                "Open", ResponseType.Accept
            );
            if (LoadStateDialog.Run() == (int)ResponseType.Accept)
            {
                LoadState(LoadStateDialog.Filename);
            }
            LoadStateDialog.Destroy();
            paused = wasPaused;
            display.widget.GrabFocus();

        }

        private void LoadROM(string path)
        {
            paused = true;
            BuildEmu();
            debuggerWindow?.Destroy();
            debuggerWindow = null;
            cartrige = new Cartridge();
            cartrige.LoadROMFile(path);
            memory.MountCartrige(cartrige);
            Logger.Write($"ROM LOADED : {path}");
            Logger.Write($"MBC : {memory.ReadByteFromAddress(0x0147): X2}");
            paused = false;
            pauseItem.Active = false;
            display.widget.GrabFocus();
        }

        private void OpenDebugger(object sender, EventArgs e)
        {
            if (debuggerWindow == null)
            {
                debuggerWindow = new DebuggerWindow(cpu, memory, gpu);
                debuggerWindow.ShowAll();
            }
            else
            {
                debuggerWindow.Present();
            }
        }

        private Stopwatch frameWatch = Stopwatch.StartNew();
        private double cycleAccumulator = 0;
        private const double CPUFrequency = 4194304.0;
        private bool EmulatorLoop()
        {
            if (paused)
            {
                frameWatch.Restart();
                cycleAccumulator = 0;
                return true;
            }
            double elapsed = frameWatch.Elapsed.TotalSeconds;
            frameWatch.Restart();
            cycleAccumulator += elapsed * CPUFrequency;
            int cyclesToRun = (int)cycleAccumulator;
            cycleAccumulator -= cyclesToRun; int cyclesThisFrame = 0;
            while (cyclesThisFrame < cyclesToRun)
            {
                int used = cpu.Cycle();
                if (used == 0)
                {
                    Logger.Write("CPU stopped because of unknown opcode or error.");
                    paused = true;
                    break;
                }
                cyclesThisFrame += used;
            }
            display.Update();
            return true;
        }

        private void OpenLogFile(object sender, EventArgs e)
        {
            Logger.LogWriter?.Flush();
            if (File.Exists(Logger.filePath))
            {
                Process.Start(new ProcessStartInfo { FileName = Logger.filePath, UseShellExecute = true });
            }
            else
            {
                MessageDialog dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok, "No log file to show!");
                dialog.Run();
                dialog.Destroy();
            }
        }

        private void OnKeyDown(object sender, KeyPressEventArgs pressArgs)
        {
            bool keyHandled = true;
            bool alreadyPressed = false;
            Logger.Write($"KEY DOWN: {pressArgs.Event.Key}");
            switch (pressArgs.Event.Key)
            {
                case Gdk.Key.w:
                case Gdk.Key.W:
                case Gdk.Key.Up:
                    alreadyPressed = upPressed;
                    upPressed = true;
                    break;

                case Gdk.Key.s:
                case Gdk.Key.S:
                case Gdk.Key.Down:
                    alreadyPressed = downPressed;
                    downPressed = true;
                    break;

                case Gdk.Key.a:
                case Gdk.Key.A:
                case Gdk.Key.Left:
                    alreadyPressed = leftPressed;
                    leftPressed = true;
                    break;

                case Gdk.Key.d:
                case Gdk.Key.D:
                case Gdk.Key.Right:
                    alreadyPressed = rightPressed;
                    rightPressed = true;
                    break;

                case Gdk.Key.z:
                case Gdk.Key.Z:
                    alreadyPressed = aPressed;
                    aPressed = true;
                    break;

                case Gdk.Key.x:
                case Gdk.Key.X:
                    alreadyPressed = bPressed;
                    bPressed = true;
                    break;
                case Gdk.Key.Return:
                case Gdk.Key.KP_Enter:
                case Gdk.Key.ISO_Enter:
                case Gdk.Key.space:
                case Gdk.Key.c:
                case Gdk.Key.C:
                    alreadyPressed = startPressed;
                    startPressed = true;
                    break;
                case Gdk.Key.BackSpace:
                case Gdk.Key.Tab:
                case Gdk.Key.Shift_R:
                case Gdk.Key.Shift_L:
                case Gdk.Key.v:
                case Gdk.Key.V:
                    alreadyPressed = selectPressed;
                    selectPressed = true;
                    break;
                default:
                    keyHandled = false;
                    break;
            }
            if (!keyHandled)
                return;

            pressArgs.RetVal = true;

            if (!alreadyPressed)
            {
                memory.RequestInterruptCall(4);
            }

        }
        private void OnKeyUp(object sender, KeyReleaseEventArgs releaseArgs)
        {
            switch (releaseArgs.Event.Key)
            {
                case Gdk.Key.w:
                case Gdk.Key.W:
                case Gdk.Key.Up:
                    upPressed = false;
                    break;

                case Gdk.Key.s:
                case Gdk.Key.S:
                case Gdk.Key.Down:
                    downPressed = false;
                    break;

                case Gdk.Key.a:
                case Gdk.Key.A:
                case Gdk.Key.Left:
                    leftPressed = false;
                    break;

                case Gdk.Key.d:
                case Gdk.Key.D:
                case Gdk.Key.Right:
                    rightPressed = false;
                    break;

                case Gdk.Key.z:
                case Gdk.Key.Z:
                    aPressed = false;
                    break;

                case Gdk.Key.x:
                case Gdk.Key.X:
                    bPressed = false;
                    break;

                case Gdk.Key.Return:
                case Gdk.Key.KP_Enter:
                case Gdk.Key.ISO_Enter:
                case Gdk.Key.space:
                case Gdk.Key.c:
                case Gdk.Key.C:
                    startPressed = false;
                    break;

                case Gdk.Key.BackSpace:
                case Gdk.Key.Tab:
                case Gdk.Key.Shift_R:
                case Gdk.Key.Shift_L:
                    selectPressed = false;
                    break;



                case Gdk.Key.v:
                case Gdk.Key.V:
                    selectPressed = false;
                    break;

                default:
                    return;
            }
            releaseArgs.RetVal = true;
        }

        public void SaveState(string filePath)
        {
            if (cartrige == null)
            {
                ErrorBox("Save State Error", "Load a ROM before saving a state.");
                return;
            }
            using (BinaryWriter stateWriter = new BinaryWriter(new FileStream(filePath, FileMode.Create)))
            {
                stateWriter.Write("GAMEBOYSAVESTATE");
                stateWriter.Write(cartrige.GetROMName());
                cartrige.Save(stateWriter);
                cpu.Save(stateWriter);
                memory.Save(stateWriter);
                gpu.Save(stateWriter);
                apu.Save(stateWriter);

            }
        }

        public void LoadState(string filePath)
        {
            if (cartrige == null)
            {
                ErrorBox("Load Save Error", "Load the ROM before loading the save state.");
                return;
            }
            using (BinaryReader stateReader = new BinaryReader(new FileStream(filePath, FileMode.Open)))
            {
                string GBStatusChecker = stateReader.ReadString();
                if (GBStatusChecker != "GAMEBOYSAVESTATE")
                {
                    ErrorBox("Load Save Error", "Invalid save file");
                    return;
                }
                string saveROMname = stateReader.ReadString();
                if (cartrige.romName.ToString() != saveROMname)
                {
                    ErrorBox("Load Save Error", "Game of save file doesnt match up with current game.");
                    return;
                }
                cartrige.Load(stateReader);
                cpu.Load(stateReader);
                memory.Load(stateReader);
                gpu.Load(stateReader);
                apu.Load(stateReader);

            }
            display.Update();
            display.widget.GrabFocus();
        }

        public void ErrorBox(string Title, string ErrorMessage)
        {
            MessageDialog Error = new MessageDialog
            (
                this,
                DialogFlags.Modal,
                MessageType.Error,
                ButtonsType.Ok,
                ErrorMessage

            );
            Error.Title = Title;
            Error.Run();
            Error.Destroy();
        }
    }
}
