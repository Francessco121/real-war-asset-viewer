using System;

namespace RealWar.Viewer.Viewers;

public interface IViewer : IDisposable
{
    string Name { get; }
    bool Open { get; }

    void Update(float deltaTime);
    void Draw(float deltaTime);
}
