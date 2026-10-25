NOT FINISHED PROJECT (Debug mode does nothing as of right now)

GameBoy Emulator (C#)

A Game Boy emulator built from scratch in C# using GTK for the graphics and interface.
I made this for my computer science NEA project.

Features:

- **CPU:**, fully functioning and accurate.
- **Graphics:**, PPU rendering using GTK, accurate enough to run games.
- **Audio:**, 4-Channel Audio, with basic timing but its good enough
- **Controls:**, D pad = WASD, A button = Z, B button = X, START = C, SELECT = V

Cartridge types supported:
Although all cartridge types are recognised, since different cartridge types work differently, this emulator cannot support them all.
Currently it supports No MBC, MBC1, MBC5 (with all their variants).

 🛠️ How to Run

1. Clone this repository:
   ```bash
   git clone [https://github.com/Theonito08/GameBoy.git](https://github.com/Theonito08/GameBoy.git)
2. Run the program
3. Choose a compatible .gb file (see the cartridge types supported)
4. Enjoy!
5. Save the game before you exit, using savestates!

This project is created strictly for educational purposes. No copyrighted ROM files or Nintendo trade secrets are included in this repository. Please provide your own legally obtained ROMs or open-source homebrew software to test.
