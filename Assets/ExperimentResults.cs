using System.Collections.Generic;
using System;
using UnityEngine;

// =============================================================
//  ExperimentResults.cs
//  ADITIVO: se conservan CalibrationData y ExperimentData (las usa
//  el ExperimentManager.cs original de Victoria, NO borrar) y se
//  agregan las clases nuevas para la calibracion repetida del ASG.
// =============================================================

// ---- ORIGINAL DE VICTORIA (no tocar: lo usa ExperimentManager.cs) ----
[Serializable]
public class CalibrationData
{
    public List<GazeSample> gazeSamples;
}

[Serializable]
public class ExperimentData
{
    public CalibrationData calibration;
    public List<ActivityResults> activities;

    public ExperimentData(List<GazeSample> calibrationSamples, List<ActivityResults> results)
    {
        calibration = new CalibrationData
        {
            gazeSamples = calibrationSamples
        };
        activities = results;
    }
}

// ---- NUEVO PARA EL ASG: calibracion por segmentos ----
// Un punto = un target + todas sus muestras crudas (sin promediar)
[Serializable]
public class CalibrationPoint
{
    public int targetIndex;
    public Vector3 targetPosition;          // posicion REAL del target (lo que faltaba)
    public List<GazeSample> gazeSamples;    // crudo
}

// Una calibracion completa (inicial o recalibracion)
[Serializable]
public class CalibrationSegment
{
    public string label;        // "initial","recal_1"..."recal_5"
    public string type;         // "full_9" | "quick_5" | "full_16"
    public int afterTrial;      // ensayo global tras el cual ocurre (0,10,20,25,37,49)
    public List<CalibrationPoint> points = new List<CalibrationPoint>();
}
