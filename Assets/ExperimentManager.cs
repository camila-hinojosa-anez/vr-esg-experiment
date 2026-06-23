using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using System.Collections.Generic;
using System;
using UnityEngine.Networking;
using TMPro;


public class ExperimentManager : MonoBehaviour
{

    public Image imageComponent;
    public Sprite[] activitiesSprites;
    public GameObject buttonP;
    public GameObject buttonQ;
    public GameObject panelMessage;
    public TMP_Text textActivity;
    public Button continueButton; 
    public GameObject botonTerminar;
    public GameObject activityPanel;
    public GameObject calibrationInstructionsPanel;
    public Button startCalibrationButton;
    public GameObject calibrationSphere;
    public GameObject finishText;

    private List<GazeSample> calibrationSamples = new List<GazeSample>();

    private int currentActivity = 1;
    public List<ActivityResults> results = new List<ActivityResults>();
    private int spriteToShow = 0;
    private float comparativoStartTime;

    Vector3 canvasCenter = new Vector3(0, 1.22f, 2.5f);
    Vector3 imageOffset = new Vector3(0, 0.081f, 0); // offset de la imagen respecto al canvas
    float imageWidth = 1.5f;   // tamaño de la imagen en metros
    float imageHeight = 1.5f;

    private bool continuarPresionado = false;
    private bool startClicked = false;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartCoroutine(StartExperiment());
    }

    private IEnumerator StartExperiment()
    {
        yield return StartCoroutine(RunCalibration());
        yield return StartCoroutine(StartActivity());
    }

    public void StartCalibration()
    {
        startClicked = true;
        calibrationInstructionsPanel.SetActive(false);
    }

    private IEnumerator RunCalibration()
    {
        yield return new WaitUntil(() => startClicked);

        //posiciones 16 esferas de calibracion 
        List<Vector3> targetPositions = new List<Vector3>();

        int divisions = 4;
        float stepX = imageWidth / (divisions - 1);
        float stepY = imageHeight / (divisions - 1);

        float[] xs = new float[divisions];
        float[] ys = new float[divisions];

        for (int i = 0; i < divisions; i++)
        {
            xs[i] = -imageWidth / 2 + i * stepX;
            ys[i] = -imageHeight / 2 + i * stepY;
        }


        Vector3 imageCenter = canvasCenter + imageOffset;

        foreach (float x in xs)
        {
            foreach (float y in ys)
            {
                targetPositions.Add(imageCenter + new Vector3(x, y, 0));
            }
        }

        List<Vector3> gazeHits = new List<Vector3>();
        List<Vector3> trueTargets = new List<Vector3>();

        Debug.Log("Iniciando calibración del eye tracking");

        int target_index = 1;

        foreach (Vector3 target in targetPositions)
        {
            EyeTrackingLogger.Instance.trackingActive = true;
            EyeTrackingLogger.Instance.currentStage = "target_"+ target_index;

            yield return StartCoroutine(ShowCalibrationSphere(target));

            //obtener las últimas muestras de gaze durante la calibración
            var sample = EyeTrackingLogger.Instance.EndAndReturnSamples();
            calibrationSamples.AddRange(sample);

            if (sample.Count > 0)
            {
                Vector3 averageGaze = Vector3.zero;
                int validSamples = 0;
                int startIndex = Mathf.Max(0, sample.Count - 30); //tomo las ultimas 30 muestras (aprox ultimo segundo) para mayor precision

                for (int i = startIndex; i < sample.Count; i++)
                {
                    if (sample[i].isValid)
                    {
                        averageGaze += sample[i].cyclopeanIntersection;
                        validSamples++;
                    }
                }

                if (validSamples > 0)
                {
                    averageGaze /= validSamples;
                    gazeHits.Add(averageGaze);
                    trueTargets.Add(target);

                    Debug.Log($"Calibración punto {trueTargets.Count}: Target={target}, Gaze={averageGaze}, Diferencia={target - averageGaze}");
                }
            }
            target_index++;
            EyeTrackingLogger.Instance.trackingActive = false;
            yield return new WaitForSeconds(0.5f); 
        }
    }

    public IEnumerator ShowCalibrationSphere(Vector3 position, float duration = 5f)
    {
        GameObject sphere = Instantiate(calibrationSphere, position, Quaternion.identity);
        sphere.SetActive(true);

        Debug.Log($"Mostrando esfera de calibración en: {position}");
        yield return new WaitForSeconds(0.5f);
        EyeTrackingLogger.Instance.ClearSamples();
        yield return new WaitForSeconds(duration - 1f);

        Destroy(sphere);
        yield return new WaitForSeconds(0.8f);
    }


    private IEnumerator StartActivity()
    {

       
        while (currentActivity < 8)
        {

            ShowMessagePanel();

            yield return new WaitUntil(() => continuarPresionado);

            continuarPresionado = false;


            EyeTrackingLogger.Instance.currentStage = "activo1";
            yield return StartCoroutine(ShowImagesActivity(currentActivity));
            yield return new WaitUntil(() => decisionMade);

            currentActivity++;
            decisionMade = false;

            buttonP.SetActive(false);
            buttonQ.SetActive(false);
        }
        imageComponent.gameObject.SetActive(false);
        botonTerminar.SetActive(true);
        finishText.SetActive(true);
        yield break;


    }

    private IEnumerator ShowImagesActivity (int  activity)
    {
        EyeTrackingLogger.Instance.currentStage = "activo1";
        imageComponent.sprite = activitiesSprites[spriteToShow];
        yield return new WaitForSeconds(15f);
        spriteToShow++;

        EyeTrackingLogger.Instance.currentStage = "activo2";
        imageComponent.sprite = activitiesSprites[spriteToShow];
        yield return new WaitForSeconds(15f);
        spriteToShow++;

        EyeTrackingLogger.Instance.currentStage = "comparativo";
        imageComponent.sprite = activitiesSprites[spriteToShow];
        spriteToShow++;
        comparativoStartTime = Time.time;

        buttonP.SetActive(true);
        buttonQ.SetActive(true);
    }

  

    public void ShowMessagePanel()
    {
        textActivity.text = $"Actividad {currentActivity}";
        panelMessage.SetActive(true);
        activityPanel.SetActive(false);
    }

    public void ContinueActivity()
    {
        panelMessage.SetActive(false);
        EyeTrackingLogger.Instance.ClearSamples();
        EyeTrackingLogger.Instance.trackingActive = true;
        continuarPresionado = true;
        activityPanel.SetActive(true);
    }

    private bool decisionMade = false;

    public void PChosen()
    {
        RegisterSelection("p");
        decisionMade = true;
    }
    public void QChosen()
    {
        RegisterSelection("q");
        decisionMade = true;
    }

    private void RegisterSelection(string chosen)
    {
        EyeTrackingLogger.Instance.trackingActive = false;
        ActivityResults newResult = new ActivityResults();
        newResult.nActivity = currentActivity;
        newResult.chosen = chosen;
        newResult.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        newResult.gazeSamples = EyeTrackingLogger.Instance.EndAndReturnSamples();
        newResult.timeToMakeDecision = Time.time - comparativoStartTime;

        results.Add(newResult);

        Debug.Log($"Guardado: Actividad {newResult.nActivity}, Eleccion {newResult.chosen}, Timestamp {newResult.timestamp}");
    }

    public void TerminarExperimento()
    {
        SaveResults();
        Application.Quit();
    }


    private void SaveResults()
    {
        var data = new ExperimentData(calibrationSamples, results);
        string json = JsonUtility.ToJson(data, true);
        Debug.Log("Resultados en JSON");

        SaveResultsLocally(json);
        // StartCoroutine(SendResultsToServer(json));
    }


    private void SaveResultsLocally(string jsonData)
    {
        string fileName = $"Resultados_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.json";
        string filePath = System.IO.Path.Combine(Application.persistentDataPath, fileName);

        System.IO.File.WriteAllText(filePath, jsonData);
        Debug.Log($"Resultados guardados localmente en: {filePath}");
    }


    private IEnumerator SendResultsToServer(string jsonData) 
    {
        string url = "https://p2761nfb-8000.brs.devtunnels.ms/";

        UnityWebRequest request = new UnityWebRequest(url, "POST");

        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        Debug.Log("Enviando datos al servidor...");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Datos enviados con exito al servidor");
            Debug.Log("Respuesta del servidor: " + request.downloadHandler.text);
        }
        else
        {
            Debug.LogError("Error al enviar datos: " + request.error);
        }
    }
}
