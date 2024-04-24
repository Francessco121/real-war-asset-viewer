using System.Numerics;

namespace RealWar.Viewer.Utils;

public static class TriUtils
{
    public static Vector3 CalculateSurfaceNormal(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 u = v2 - v1;
        Vector3 v = v3 - v1;

        return Vector3.Normalize(Vector3.Cross(u, v));
    }
}
