using System;

public interface IFaceDetector
{
    bool HasFace { get; }

    event Action FaceDetected;
    event Action FaceLost;
}