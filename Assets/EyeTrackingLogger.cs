using UnityEngine;
using System.Collections.Generic;
using Meta.XR;
using Meta.XR.Editor.Id;

public class EyeTrackingLogger : MonoBehaviour
{
    //eyetracking
    public static EyeTrackingLogger Instance { get; private set; }
    public Transform leftEyeGaze;
    public Transform rightEyeGaze;
    public bool trackingActive = false;
    public string currentStage = "undefined";

    //cconfiguracion canvas
    public float canvasDistance = 2.5f;

    //facetracking
    public bool useFaceTrackingValidation = true;
    public float blinkThreshold = 0.5f;

   //rayos
    public bool showGazeRays = false;
    public LineRenderer leftEyeRay;
    public LineRenderer rightEyeRay;
    public LineRenderer cyclopeanRay;
    public Material rayMaterial;

    private List<GazeSample> currentSamples = new List<GazeSample>();
    private OVRFaceExpressions faceExpressions;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            InitializeComponents();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void InitializeComponents()
    {
        if (useFaceTrackingValidation)
        {
            faceExpressions = FindFirstObjectByType<OVRFaceExpressions>();
            if (faceExpressions == null)
            {
                Debug.LogWarning("OVRFaceExpressions no encontrado. Face tracking deshabilitado.");
                useFaceTrackingValidation = false;
            }
        }

        if (showGazeRays)
        {
            SetupDebugRays();
        }
    }

    void SetupDebugRays()
    {
        if (leftEyeRay == null)
        {
            GameObject leftRayObj = new GameObject("LeftEyeRay");
            leftRayObj.transform.parent = transform;
            leftEyeRay = leftRayObj.AddComponent<LineRenderer>();
        }

        if (rightEyeRay == null)
        {
            GameObject rightRayObj = new GameObject("RightEyeRay");
            rightRayObj.transform.parent = transform;
            rightEyeRay = rightRayObj.AddComponent<LineRenderer>();
        }

        if (cyclopeanRay == null)
        {
            GameObject cyclopeanRayObj = new GameObject("CyclopeanRay");
            cyclopeanRayObj.transform.parent = transform;
            cyclopeanRay = cyclopeanRayObj.AddComponent<LineRenderer>();
        }
        ConfigureLineRenderer(leftEyeRay, Color.red, 0.002f);
        ConfigureLineRenderer(rightEyeRay, Color.blue, 0.002f);
        ConfigureLineRenderer(cyclopeanRay, Color.green, 0.004f);
    }

    void ConfigureLineRenderer(LineRenderer line, Color color, float width)
    {
        line.material = rayMaterial != null ? rayMaterial : new Material(Shader.Find("Sprites/Default"));
        line.startWidth = width;
        line.endWidth = width;
        line.positionCount = 2;
        line.useWorldSpace = true;

 
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(color, 0.0f), new GradientColorKey(color, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) }
        );
        line.colorGradient = gradient;
    }

    void Update()
    {
        if (!trackingActive || leftEyeGaze == null || rightEyeGaze == null)
        {
            if (showGazeRays) HideRays();
            return;
        }

        if (useFaceTrackingValidation && IsBlinking())
        {
            if (showGazeRays) HideRays();
            return; 
        }

        //VECTORES ORIGNE Y DIRECCION
        Vector3 leftOrigin = leftEyeGaze.position;
        Vector3 rightOrigin = rightEyeGaze.position;
        Vector3 leftDirection = leftEyeGaze.forward;
        Vector3 rightDirection = rightEyeGaze.forward;

        //CALCULO RAYO CICLOPEO(?)
        Vector3 cyclopeanOrigin = (leftOrigin + rightOrigin) / 2f;
        Vector3 cyclopeanDirection = ((leftDirection + rightDirection) / 2f).normalized;

        //CALCULO INTERSECCIONES
        Vector3 leftIntersection = CalculateCanvasIntersection(leftOrigin, leftDirection);
        Vector3 rightIntersection = CalculateCanvasIntersection(rightOrigin, rightDirection);
        Vector3 cyclopeanIntersection = CalculateCanvasIntersection(cyclopeanOrigin, cyclopeanDirection);

        
        GazeSample sample = new GazeSample
        {
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            leftOrigin = leftOrigin,
            leftDirection = leftDirection,
            leftEyeRotation = leftEyeGaze.rotation,
            rightOrigin = rightOrigin,
            rightDirection = rightDirection,
            rightEyeRotation = rightEyeGaze.rotation,
            intersectionLeft = leftIntersection,
            intersectionRight = rightIntersection,
            cyclopeanOrigin = cyclopeanOrigin,
            cyclopeanDirection = cyclopeanDirection,
            cyclopeanIntersection = cyclopeanIntersection,
            stage = currentStage,
            isValid = true // en una de esas no es necesario ya que siempre va a decir true por que ya se reviso el parpadeo
        };

        currentSamples.Add(sample);

        if (showGazeRays)
        {
            UpdateRayVisualization(leftOrigin, leftDirection, rightOrigin, rightDirection,
                                 cyclopeanOrigin, cyclopeanDirection);
        }
    }

    Vector3 CalculateCanvasIntersection(Vector3 origin, Vector3 direction)
    {
        if (Mathf.Approximately(direction.z, 0f))
        {
            return origin;
        }

        float t = (canvasDistance - origin.z) / direction.z;
        return origin + direction * t;
    }

    bool IsBlinking()
    {
        if (faceExpressions == null) return false;

        //valores de parpadeo
        float leftEyeBlink = faceExpressions[OVRFaceExpressions.FaceExpression.EyesClosedL];
        float rightEyeBlink = faceExpressions[OVRFaceExpressions.FaceExpression.EyesClosedR];

        return (leftEyeBlink > blinkThreshold || rightEyeBlink > blinkThreshold);
    }

    void UpdateRayVisualization(Vector3 leftOrigin, Vector3 leftDir, Vector3 rightOrigin, Vector3 rightDir,
                               Vector3 cyclopeanOrigin, Vector3 cyclopeanDir)
    {
        float rayLength = 3f; 

        //rayo ojo izquierdo (rojo)
        leftEyeRay.SetPosition(0, leftOrigin);
        leftEyeRay.SetPosition(1, leftOrigin + leftDir * rayLength);

        //rayo ojo derecho (azul)
        rightEyeRay.SetPosition(0, rightOrigin);
        rightEyeRay.SetPosition(1, rightOrigin + rightDir * rayLength);

        //rayo ciclopeo (verde, más grueso)
        cyclopeanRay.SetPosition(0, cyclopeanOrigin);
        cyclopeanRay.SetPosition(1, cyclopeanOrigin + cyclopeanDir * rayLength);

        leftEyeRay.enabled = false;
        rightEyeRay.enabled = false;
        cyclopeanRay.enabled = false;
    }

    void HideRays()
    {
        if (leftEyeRay != null) leftEyeRay.enabled = false;
        if (rightEyeRay != null) rightEyeRay.enabled = false;
        if (cyclopeanRay != null) cyclopeanRay.enabled = false;
    }

    public List<GazeSample> EndAndReturnSamples()
    {
        var toReturn = new List<GazeSample>(currentSamples);
        currentSamples.Clear();
        return toReturn;
    }

    public void ClearSamples()
    {
        currentSamples.Clear();
    }

    public Vector3? GetCurrentGazePoint()
    {
        if (currentSamples.Count > 0)
        {
            var lastSample = currentSamples[currentSamples.Count - 1];
            return lastSample.cyclopeanIntersection;
        }
        return null;
    }
}