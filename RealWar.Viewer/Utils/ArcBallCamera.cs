using System.Numerics;

namespace RealWar.Viewer.Utils;

public class ArcBallCamera
{
    public ref readonly Vector3 Position => ref position;

    public ref readonly Matrix4x4 ViewMatrix => ref viewMatrix;
    public ref readonly Matrix4x4 ProjectionMatrix => ref projectionMatrix;

    public Vector3 Target = Vector3.Zero;
    public float Pitch;
    public float Yaw;
    public float Radius = 10;
    public float FieldOfView = MathUtils.ToRadians(50);
    public float AspectRatio = 1;
    public float NearPlane = 0.1f;
    public float FarPlane = 10000f;

    Vector3 position;
    Matrix4x4 viewMatrix;
    Matrix4x4 projectionMatrix;

    public ArcBallCamera()
    {
        Update();
    }

    public Vector3 TransformXYRotation(Vector3 vec)
    {
        vec = Vector3.Transform(vec,
            Matrix4x4.CreateRotationX(Pitch) * Matrix4x4.CreateRotationY(Yaw));

        return -new Vector3(-vec.X, -vec.Y, vec.Z);
    }

    public void Update()
    {
        Matrix4x4 rotation = Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateRotationX(Pitch);
        Matrix4x4.Invert(rotation, out rotation);
        Vector3 direction = Vector3.TransformNormal(-Vector3.UnitZ, rotation);
        position = Target - (direction * Radius);

        viewMatrix = Matrix4x4.CreateLookAt(
            cameraPosition: position,
            cameraTarget: Target,
            Vector3.UnitY);

        projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            fieldOfView: FieldOfView,
            aspectRatio: AspectRatio,
            nearPlaneDistance: NearPlane,
            farPlaneDistance: FarPlane);
    }
}
