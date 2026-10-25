using Cairo;
using Gtk;
using System;
using System.CodeDom;

namespace GameBoyEmu.Graphics
{
    public class GameBoyDisplay
    {
        private readonly GameBoyFrameBuffer frameBuffer;
        private readonly DrawingArea drawingArea;

        private readonly byte[] pixelData;
        private readonly ImageSurface surface;

        private const int width = 160;
        private const int height = 144;

        public GameBoyDisplay(GameBoyFrameBuffer frameBufferIn)
        {
            frameBuffer = frameBufferIn;
            drawingArea = new DrawingArea();
            pixelData = new byte[width * height * 4];
            surface = new ImageSurface(pixelData, Format.Rgb24, width, height, width * 4);
            drawingArea.Drawn += DrawDisplay;
        }
        public DrawingArea widget
        {
            get { return drawingArea; }
        }
        private void DrawDisplay(object sender, DrawnArgs args)
        {
            Cairo.Context cr = args.Cr;
            double scaleX = drawingArea.AllocatedWidth / (double)width;
            double scaleY = drawingArea.AllocatedHeight / (double)height;
            cr.Scale(scaleX, scaleY);
            cr.SetSourceSurface(surface, 0, 0);
            Cairo.SurfacePattern pattern = (Cairo.SurfacePattern)cr.GetSource();
            pattern.Filter = Cairo.Filter.Nearest;
            cr.Paint();
            pattern.Dispose();
        }
        public void Update()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte shade = frameBuffer.GetPixel(x, y);

                    byte red;
                    byte green;
                    byte blue;

                    switch (shade)
                    {
                        case 0:
                            red = 136;
                            green = 160;
                            blue = 72;
                            break;
                        case 1:
                            red = 72;
                            green = 104;
                            blue = 48;
                            break;
                        case 2:
                            red = 40;
                            green = 64;
                            blue = 32;
                            break;
                        case 3:
                            red = 24;
                            green = 40;
                            blue = 8;
                            break;
                        default:
                            red = 255;
                            green = 255;
                            blue = 255;
                            break;
                    }

                    int index = (y * width + x) * 4;
                    pixelData[index] = blue;
                    pixelData[index + 1] = green;
                    pixelData[index + 2] = red;
                    pixelData[index + 3] = 0;
                }
            }
            surface.MarkDirty();
            drawingArea.QueueDraw();
        }
    }
}
