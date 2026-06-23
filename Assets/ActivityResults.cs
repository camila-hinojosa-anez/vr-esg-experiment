using System.Collections.Generic;

using UnityEngine;

[System.Serializable]
public class ActivityResults
{
    public int nActivity;
    public string chosen;
    public string timestamp;
    public float timeToMakeDecision;
    public List<GazeSample> gazeSamples;

}

[System.Serializable]
public class ActivitiesList
{
    public List<ActivityResults> activities;

    public ActivitiesList(List<ActivityResults> activities)
    {
        this.activities = activities;
    }   
}

