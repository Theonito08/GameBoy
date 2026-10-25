using GameBoyEmu.Cartrige;
using GameBoyEmu.Memory;
using GameBoyEmu.Processor;
using GameBoyEmu.Visual;
using Gtk;
using System;
using System.Text;


namespace GameBoyEmu.Debugger
{
    public class DebuggerWindow : Window
    {

        private Label cpuLabel;
        private Label gpuLabel;
        private Label memoryLabel;
        private Entry memoryRangeStartEntry;
        private Entry memoryRangeEndEntry;
        private TextView memoryTextView;
        TextView opcodeTextView = new TextView();

        private CPU cpu;
        private MEMORY memory;
        private GPU gpu;
        public DebuggerWindow(CPU cpu, MEMORY memory, GPU gpu) : base("Debugger")
        {
            this.cpu = cpu;
            this.memory = memory;
            this.gpu = gpu;
            opcodeTextView.Editable = false;
            opcodeTextView.WrapMode = WrapMode.None;
            opcodeTextView.ModifyFont(Pango.FontDescription.FromString("Monospace 10"));
            SetDefaultSize(600, 500);
            VBox layout = new VBox(false, 5);
            Notebook notebook = new Notebook();

            //CPU
            VBox cpuPage = new VBox(false, 5);
            Frame cpuFrame = new Frame("CPU STATUS");
            
            cpuLabel = new Label("");
            cpuLabel.UseMarkup = true;
            
            Alignment alignCPU = new Alignment(0, 0, 0, 0);
            alignCPU.Add(cpuLabel);
            
            cpuFrame.Add(alignCPU);
            cpuPage.PackStart(cpuFrame, false, true, 5);
            
            notebook.AppendPage(cpuPage, new Label("CPU"));

            //GPU
            VBox gpuPage = new VBox(false, 5);
            Frame gpuFrame = new Frame("GPU STATUS");
            
            gpuLabel = new Label("");
            gpuLabel.UseMarkup = true;
            
            Alignment alignGPU = new Alignment(0, 0, 0, 0);
            alignGPU.Add(gpuLabel);
            
            gpuFrame.Add(alignGPU);
            gpuPage.PackStart(gpuFrame, false, true, 5);
            
            notebook.AppendPage(gpuPage, new Label("GPU"));

            //MEMORY
            VBox memoryPage = new VBox(false, 5);
            Frame memoryFrame = new Frame("MEMORY STATUS");
            
            memoryLabel = new Label("");
            memoryLabel.UseMarkup = true;
            
            Alignment alignMEMORY = new Alignment(0, 0, 0, 0);
            alignMEMORY.Add(memoryLabel);
           
            memoryFrame.Add(alignMEMORY);
            memoryPage.PackStart(memoryFrame, false, true, 5);
            
            Frame memoryRangeViewerFrame = new Frame("MEMORY VIEWER (HEX ONLY)");
            VBox memoryRangeViewerBox = new VBox(false, 5);
            HBox memoryAddressBox = new HBox(false, 5);
            
            memoryRangeStartEntry = new Entry();
            memoryRangeStartEntry.PlaceholderText = "Start address";
            memoryRangeEndEntry = new Entry();
            memoryRangeEndEntry.PlaceholderText = "End address";
            
            Button viewMemoryButton = new Button("View/Refresh");
            viewMemoryButton.Clicked += ViewMemoryAtAddressRange;

            memoryAddressBox.PackStart(memoryRangeStartEntry, true, true, 5);
            memoryAddressBox.PackStart(memoryRangeEndEntry, true, true, 5);
            memoryAddressBox.PackStart(viewMemoryButton, false, false, 5);
            memoryRangeViewerBox.PackStart(memoryAddressBox, false, false, 5);
            
            memoryTextView = new TextView();
            memoryTextView.Editable = false;
            memoryTextView.WrapMode = WrapMode.None;
            memoryTextView.ModifyFont(Pango.FontDescription.FromString("Monospace 10"));

            ScrolledWindow memoryScroll = new ScrolledWindow();
            memoryScroll.SetSizeRequest(-1, 200);
            memoryScroll.Add(memoryTextView);
            memoryRangeViewerBox.PackStart(memoryScroll, true, true, 5);
            memoryRangeViewerFrame.Add(memoryRangeViewerBox);
            memoryPage.PackStart(memoryRangeViewerFrame, true, true, 5);

            notebook.AppendPage(memoryPage, new Label("MEMORY"));

            //OPCODES
            VBox opcodePage = new VBox(false, 5);
            Button refreshOpcodeButton = new Button("Load Last 500 Opcodes");
            refreshOpcodeButton.Clicked += ShowOpcodes;
            opcodePage.PackStart(refreshOpcodeButton, false, false, 5);
            
            Frame opcodeFrame = new Frame("LAST EXECUTED OPCODES");
            ScrolledWindow scroll = new ScrolledWindow();
            scroll.Add(opcodeTextView);
            opcodeFrame.Add(scroll);
            
            opcodePage.PackStart(opcodeFrame, true, true, 5);
            notebook.AppendPage(opcodePage, new Label("Opcodes"));

            //SETUP
            layout.PackStart(notebook, true, true, 5);
            Add(layout);
            GLib.Timeout.Add(50, UpdateDebugger);
            ShowAll();
        }
        protected override bool OnDeleteEvent(Gdk.Event e)
        {
            Hide();
            return true;
        }

        private bool UpdateDebugger()
        {
            SetUpCPU();
            SetUpGPU();
            SetUpMEMORY();
            return true;
        }
        
        private void SetUpCPU()
        {
            cpuLabel.Text = $"A : {cpu.Registers.A:X2} \nF : {cpu.Registers.F:X2} \nB : {cpu.Registers.B:X2} \nC : {cpu.Registers.C:X2} \nD : {cpu.Registers.D:X2} \nE : {cpu.Registers.E:X2} \nH : {cpu.Registers.H:X2} \nL : {cpu.Registers.L:X2} \nPC : {cpu.Registers.PC:X4} \nSP : {cpu.Registers.SP:X4}\n\n";

        }
        private void SetUpGPU()
        {
            gpuLabel.Text = "";
            gpuLabel.Text += $"LCDC : {gpu.GetLCDC()} \nSTAT : {gpu.GetSTAT()}\n\n";
            gpuLabel.Text += $"LY : {gpu.GetLY()} \nLYC : {gpu.GetLYC()}\n\n";
            gpuLabel.Text += $"SCX : {gpu.GetSCX()} \nSCY : {gpu.GetSCY()}\n\n";
            gpuLabel.Text += $"WX : {gpu.GetWX()} \nWY : {gpu.GetWY()}\n\n";
            gpuLabel.Text += $"BGP : {gpu.GetBGP()} \nOBP0 : {gpu.GetOBP0()} \nOBP1 : {gpu.GetOBP1()}\n\n";
            gpuLabel.Text += $"LCD Enabled : {gpu.GetLCDEnabled()} \nBG Enabled : {gpu.GetBGEnabled()} \nSprites Enabled : {gpu.GetSpritesEnabled()} \nWindow Enabled : {gpu.GetWindowEnabled()}\n\n";
            gpuLabel.Text += $"Sprite Size : {gpu.GetSpriteSize()} \nBG Tile Map : {gpu.GetBGTileMap()} \nTile Data : {gpu.GetTileData()} \nWindow Tile Map : {gpu.GetWindowTileMap()} \nScanline Mode : {gpu.GetScanLineMode()}\n\n";
            gpuLabel.Text += $"Current Scanline : {gpu.GetCurrentScanLine()} \nCycles Into Line : {gpu.GetCyclesIntoLine()}";

        }
        private void SetUpMEMORY()
        {
            memoryLabel.Text = $"";
            memoryLabel.Text += $"MBC Type : {memory.GetMBCType()}\n\n";
            memoryLabel.Text += $"DIV : {memory.ReadByteFromAddress(MEMORY.DIV)}\nTIMA : {memory.ReadByteFromAddress(MEMORY.TIMA)} \n";
            memoryLabel.Text += $"TMA : {memory.ReadByteFromAddress(MEMORY.TMA)}\n TAC : {memory.ReadByteFromAddress(MEMORY.TAC)}\n\n";
            memoryLabel.Text += $"OAM DMA Active : {memory.OAMDMAActive}";
        }
        private void ShowOpcodes(object sender, EventArgs e)
        {
            opcodeTextView.Buffer.Text = string.Join("\n", cpu.opcodeQueue.GetOpcodeInfo()
            );
        }

        private void ViewMemoryAtAddressRange(object sender, EventArgs e)
        {
            try
            {
                ushort startAddress = Convert.ToUInt16(memoryRangeStartEntry.Text, 16);
                ushort endAddress = Convert.ToUInt16(memoryRangeEndEntry.Text, 16);
                if(startAddress > endAddress)
                {
                    memoryTextView.Buffer.Text = "Start address has to be a smaller value than the endAddress";
                    return;
                }
                StringBuilder outputMemoryView = new StringBuilder();
                for(int memoryAddress = startAddress; memoryAddress <= endAddress; memoryAddress += 16)
                {
                    outputMemoryView.Append($"{memoryAddress:X4}: ");
                    for(int memoryOffset = 0; memoryOffset < 16 && memoryAddress + memoryOffset <= endAddress; memoryOffset++)
                    {
                        byte valueAtAddress = memory.ReadByteFromAddress((ushort)(memoryAddress + memoryOffset));
                        outputMemoryView.Append($"{valueAtAddress:X2} ");
                    }
                    outputMemoryView.AppendLine();
                }
                memoryTextView.Buffer.Text = outputMemoryView.ToString();
            }
            catch
            {
                memoryTextView.Buffer.Text = "Invalid memory address. Please enter HEX values within 0000-FFFF ONLY";
            }
        }
    }
}
