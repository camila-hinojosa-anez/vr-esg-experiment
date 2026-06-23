using System.Collections.Generic;
using System;

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
