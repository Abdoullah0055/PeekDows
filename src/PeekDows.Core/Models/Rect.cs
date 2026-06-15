namespace PeekDows.Core.Models;

public readonly record struct Rect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}
