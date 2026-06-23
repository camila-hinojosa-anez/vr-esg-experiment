using UnityEngine;

[System.Serializable]
public class GazeSample
{
    public string timestamp;
    public string stage;
    public bool isValid = true;

    //ojo izquierdo
    public Vector3 leftOrigin;
    public Vector3 leftDirection;
    public Quaternion leftEyeRotation;
    public Vector3 intersectionLeft;

    //ojo derecho
    public Vector3 rightOrigin;
    public Vector3 rightDirection;
    public Quaternion rightEyeRotation;
    public Vector3 intersectionRight;

    //ciclopeo
    public Vector3 cyclopeanOrigin;
    public Vector3 cyclopeanDirection;
    public Vector3 cyclopeanIntersection;

}