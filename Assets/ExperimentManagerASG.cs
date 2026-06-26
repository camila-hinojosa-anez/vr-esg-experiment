// ============================================================================
//  ExperimentManagerASG.cs  —  Versión VR del experimento "Drivers of Sustainable Finance"
//  FONDECYT 11230513 · adaptación del esqueleto VR de Victoria Guerriero al paradigma ASG.
//
//  PRINCIPIO: la tecnología de Victoria NO se modifica.
//   - Captura de gaze: EyeTrackingLogger.cs / GazeSample.cs  (sin cambios)
//   - Calibración 16 pts 4x4: se reusa verbatim (abajo, sección CALIBRACIÓN)
//   - Input: botones de UI P/Q activados por el rayo del control (PChosen/QChosen)  (sin cambios)
//  Lo único nuevo es ESTE orquestador: lee los CSV y corre el flujo ASG con los tiempos
//  exactos del .osexp web (ver ESPEC_TIEMPOS_VR.md).
//
//  ──────────────────────────────────────────────────────────────────────────
//  SETUP EN UNITY (una vez):
//   1) Carpeta  Assets/Resources/csv/   con los 9 .csv (fase1_block1..3, fase2_block1..3,
//      practica_fase1, practica_fase2, cal_points). Unity los importa como TextAsset.
//   2) Carpeta  Assets/Resources/pool/  con los 636 PNG del pool de la web, importados
//      como Sprite (Texture Type = Sprite (2D and UI)).
//   3) Quitar el componente ExperimentManager viejo del GameObject y agregar ESTE
//      (ExperimentManagerASG). Volver a asignar en el Inspector los mismos refs
//      (imageComponent, buttonP, buttonQ, paneles, calibrationSphere, etc.) + fixationCross.
//   4) Re-apuntar los onClick de los botones de UI a los métodos de ESTE componente:
//      P -> PChosen, Q -> QChosen, Continuar -> ContinueActivity,
//      EmpezarCalibración -> StartCalibration, Terminar -> TerminarExperimento.
//   5) Geometría 4:3: ajustar el RectTransform de la Image a proporción 1024x768 para que
//      el estímulo NO se deforme (imageWidth/imageHeight abajo = 1.5 x 1.125 m, deben
//      coincidir con el tamaño real mostrado en la escena).
//
//  PARA UNA PRUEBA RÁPIDA: poner trialsPerBlockCap = 2 y runCalibration = false en el
//  Inspector → corre pocos ensayos sin la calibración larga. Luego volver a 0 / true.
// ============================================================================

using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class ExperimentManagerASG : MonoBehaviour
{
    [Header("Referencias UI (mismas que el manager de Victoria)")]
    public Image imageComponent;          // muestra los estímulos
    public GameObject fixationCross;       // NUEVO: una "+" centrada (puede ser TMP). Si es null, se omite.
    public GameObject buttonP;             // botón de UI P (activado por rayo del control)
    public GameObject buttonQ;             // botón de UI Q
    public GameObject panelMessage;        // panel de instrucciones / "continuar"
    public TMP_Text textActivity;          // texto dentro del panel de instrucciones
    public GameObject activityPanel;       // panel donde vive la imagen del estímulo
    public GameObject calibrationInstructionsPanel;
    public GameObject calibrationSphere;   // esfera de calibración (prefab/objeto)
    public GameObject botonTerminar;
    public GameObject finishText;

    [Header("Flags de ejecución (para pruebas)")]
    public bool runCalibration = true;     // false = saltar la calibración larga al testear
    public bool runPractice = false;       // práctica (sigue en teclas B/C en la web; ver nota)
    public bool runFase1 = true;
    public bool runFase2 = true;
    public bool doRecalibration = true;    // recalibración entre bloques / fases (reusa la rejilla 16 pts)
    public int trialsPerBlockCap = 0;      // 0 = todos; >0 = corta a N ensayos por bloque (smoke test)
    public bool devKeyboardInput = false;  // SOLO DESARROLLO: responder con teclas P/Q del teclado (sin visor). En el visor real, dejar en false.

    [Header("Geometría del plano (4:3, debe coincidir con la escena)")]
    public Vector3 canvasCenter = new Vector3(0, 1.22f, 2.5f);
    public Vector3 imageOffset = new Vector3(0, 0.081f, 0);
    public float imageWidth = 1.5f;       // antes 1.5
    public float imageHeight = 1.125f;     // antes 1.5  -> 4:3 (1.5 * 768/1024)

    public const string EXPERIMENT_VERSION = "VR_ASG_A1";

    // ---- estado interno ----
    private readonly List<CalibrationSegment> calibrations = new List<CalibrationSegment>();
    private readonly List<TrialResultsASG> trials = new List<TrialResultsASG>();
    private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private ExperimentMetaASG meta = new ExperimentMetaASG();

    private bool startClicked = false;
    private bool continuePressed = false;

    private bool awaitingResponse = false;
    private string responseKey = null;             // "p" / "q" / null
    private double responseTimeRealtime = -1;
    private string lastKey; private float lastRtMs; private double lastOnsetRealtime;

    private double expStartRealtime;

    private readonly Dictionary<string, string> instrucciones = new Dictionary<string, string>();

    // =====================================================================
    //  ARRANQUE
    // =====================================================================
    void Start()
    {
        expStartRealtime = Time.realtimeSinceStartupAsDouble;
        meta.experiment_version = EXPERIMENT_VERSION;
        meta.timestamp_start = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        LoadInstructions();
        StartCoroutine(RunExperiment());
    }

    void Update()
    {
        if (!devKeyboardInput) return; // atajos SOLO de desarrollo; no afectan el input real (rayo+botón)

        if (awaitingResponse)
        {
            if (Input.GetKeyDown(KeyCode.P)) RegisterKey("p");
            else if (Input.GetKeyDown(KeyCode.Q)) RegisterKey("q");
            return;
        }
        // Espacio/Enter avanza la pantalla activa (en el visor lo hacen los botones Comenzar/Continuar con el control)
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            if (calibrationInstructionsPanel != null && calibrationInstructionsPanel.activeSelf && !startClicked)
                StartCalibration();
            else if (panelMessage != null && panelMessage.activeSelf && !continuePressed)
                ContinueActivity();
        }
    }

    private IEnumerator RunExperiment()
    {
        // posición de los botones P/Q (requisito hipótesis input, ESPEC §6.b del plan)
        if (buttonP != null) meta.buttonP_world = buttonP.transform.position;
        if (buttonQ != null) meta.buttonQ_world = buttonQ.transform.position;

        SetButtons(false);
        if (fixationCross != null) fixationCross.SetActive(false);
        if (calibrationInstructionsPanel != null) calibrationInstructionsPanel.SetActive(false);
        if (botonTerminar != null) botonTerminar.SetActive(false);
        if (finishText != null) finishText.SetActive(false);

        // Bienvenida + preparación
        yield return StartCoroutine(ShowInstr("bienvenida", "Bienvenida"));
        yield return StartCoroutine(ShowInstr("preparacion", "Preparacion"));

        // Calibración inicial (con su instrucción)
        if (runCalibration)
        {
            yield return StartCoroutine(ShowInstr("instrucciones_calibracion", "Calibracion"));
            yield return StartCoroutine(Calibrate(false, "initial", 9, 0));
        }

        // ----- PRÁCTICA (ambas fases, juntas al inicio — orden canónico web/lab) -----
        if (runPractice && (runFase1 || runFase2))
        {
            yield return StartCoroutine(ShowInstr("aviso_practica", "Practica"));
            if (runFase1) yield return StartCoroutine(RunGuidedPractice(1));
            if (runFase2) yield return StartCoroutine(RunGuidedPractice(2));
            yield return StartCoroutine(ShowInstr("transicion_post_practica", "Comienza el experimento"));
        }

        // ----- FASE 1 -----
        if (runFase1)
        {
            yield return StartCoroutine(ShowInstr("instr_fase1", "Fase 1"));
            yield return StartCoroutine(RunPhase(1,
                new[] { "csv/fase1_block1", "csv/fase1_block2", "csv/fase1_block3" }, false));
        }

        // ----- Transición a FASE 2 (recalibración) -----
        if (runFase1 && runFase2)
        {
            yield return StartCoroutine(ShowInstr("instr_transicion_f2", "Fase 2"));
            if (doRecalibration) yield return StartCoroutine(Calibrate(false, "recal_3", 9, 25));
        }

        // ----- FASE 2 -----
        if (runFase2)
        {
            yield return StartCoroutine(ShowInstr("instr_fase2", "Fase 2"));
            yield return StartCoroutine(RunPhase(2,
                new[] { "csv/fase2_block1", "csv/fase2_block2", "csv/fase2_block3" }, false));
        }

        // guardar automáticamente, luego pantalla final
        SaveResults();
        yield return StartCoroutine(ShowInstr("pantalla_fin", "Listo, gracias por participar"));

        // fin
        if (imageComponent != null) imageComponent.enabled = false;
        if (fixationCross != null) fixationCross.SetActive(false);
        SetButtons(false);
        if (botonTerminar != null) botonTerminar.SetActive(true);
        if (finishText != null) finishText.SetActive(true);
    }

    // =====================================================================
    //  INSTRUCCIONES (texto canónico desde Resources/instrucciones_es.txt)
    // =====================================================================
    private void LoadInstructions()
    {
        TextAsset ta = Resources.Load<TextAsset>("instrucciones_es");
        if (ta == null) { Debug.LogWarning("No se encontró Resources/instrucciones_es.txt; se usarán títulos cortos."); return; }
        string cur = null; var sb = new System.Text.StringBuilder();
        foreach (string raw in ta.text.Replace("\r", "").Split('\n'))
        {
            string line = raw;
            string t = line.Trim();
            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                if (cur != null) instrucciones[cur] = sb.ToString().Trim();
                cur = t.Substring(1, t.Length - 2).Trim();
                sb.Clear();
            }
            else if (cur != null) { sb.Append(line); sb.Append('\n'); }
        }
        if (cur != null) instrucciones[cur] = sb.ToString().Trim();
        Debug.Log($"Instrucciones cargadas: {instrucciones.Count}");
    }

    private IEnumerator ShowInstr(string id, string fallback)
    {
        string text = (instrucciones.TryGetValue(id, out var v) && !string.IsNullOrEmpty(v)) ? v : fallback;
        ShowInstruction(text);
        continuePressed = false;
        yield return new WaitUntil(() => continuePressed);
        continuePressed = false;
    }

    // =====================================================================
    //  FLUJO DE FASE / BLOQUE / ENSAYO
    // =====================================================================
    private IEnumerator RunPhase(int fase, string[] blockCsvs, bool isPractice = false)
    {
        for (int b = 0; b < blockCsvs.Length; b++)
        {
            // instrucción especial de supresión verbal antes del bloque 3 de Fase 1
            if (fase == 1 && b == 2)
                yield return StartCoroutine(ShowInstr("instr_fase1_b3", "Repite en voz alta 'mamma mia' sin parar"));

            List<Dictionary<string, string>> rows = ParseCsv(blockCsvs[b]);
            if (rows == null || rows.Count == 0) { Debug.LogWarning("CSV vacío: " + blockCsvs[b]); continue; }

            int cap = (trialsPerBlockCap > 0) ? Mathf.Min(trialsPerBlockCap, rows.Count) : rows.Count;
            for (int i = 0; i < cap; i++)
                yield return StartCoroutine(RunTrial(rows[i], fase, b + 1, isPractice));

            // recalibración rápida entre bloques (no después del último)
            if (doRecalibration && !isPractice && b < blockCsvs.Length - 1)
            {
                string recalLabel; int afterT;
                if (fase == 1) { recalLabel = (b == 0) ? "recal_1" : "recal_2"; afterT = (b == 0) ? 10 : 20; }
                else           { recalLabel = (b == 0) ? "recal_4" : "recal_5"; afterT = (b == 0) ? 37 : 49; }
                yield return StartCoroutine(ShowInstr("recal_rapida_intro", "Recalibracion - mira los puntos"));
                yield return StartCoroutine(Calibrate(false, recalLabel, 5, afterT));
            }
        }
    }

    // Corre una lista de pasos acumulando respuestas/gaze en el trial dado
    private IEnumerator RunSteps(List<TrialStep> steps, TrialResultsASG trial)
    {
        foreach (TrialStep step in steps)
        {
            EyeTrackingLogger.Instance.currentStage = step.stage;
            if (step.isFixation) ShowFixation();
            else ShowImage(step.sprite);

            if (step.waitsResponse)
            {
                SetButtons(true);
                yield return StartCoroutine(WaitForResponse(step.timeoutMs));
                SetButtons(false);

                ResponseRecordASG rec = BuildRecord(step, lastKey, lastRtMs, lastOnsetRealtime);
                trial.responses.Add(rec);
                if (step.field == "decision")
                {
                    trial.decision_key = rec.key;
                    trial.decision_choice = rec.meaning;
                    trial.decision_rt_ms = rec.rt_ms;
                }
            }
            else
            {
                yield return new WaitForSeconds(step.durationMs / 1000f);
            }
        }
    }

    private TrialResultsASG NewTrial(Dictionary<string, string> row, int fase, int block, bool isPractice)
    {
        return new TrialResultsASG
        {
            trial = Get(row, "trial"),
            fase = fase,
            block = block,
            is_practice = isPractice,
            period_id = row.ContainsKey("period_id") ? row["period_id"] : "",
            responses = new List<ResponseRecordASG>()
        };
    }

    private IEnumerator RunTrial(Dictionary<string, string> row, int fase, int block, bool isPractice)
    {
        var trial = NewTrial(row, fase, block, isPractice);
        EyeTrackingLogger.Instance.ClearSamples();
        EyeTrackingLogger.Instance.trackingActive = true;

        yield return StartCoroutine(RunSteps(BuildStageA(row, fase), trial));
        yield return StartCoroutine(RunSteps(BuildStageB(row, fase), trial));
        yield return StartCoroutine(RunSteps(BuildStageC(row, fase), trial));

        EyeTrackingLogger.Instance.trackingActive = false;
        trial.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        trial.gazeSamples = EyeTrackingLogger.Instance.EndAndReturnSamples();
        trials.Add(trial);
        Debug.Log($"Trial {trial.trial} (F{fase} B{block}{(isPractice ? " PRACTICA" : "")}) guardado. Respuestas={trial.responses.Count}, gaze={trial.gazeSamples.Count}");
    }

    // Práctica GUIADA por partes (A → B → C), con una instrucción antes de cada parte (igual que web/lab)
    private IEnumerator RunGuidedPractice(int fase)
    {
        string csv = (fase == 1) ? "csv/practica_fase1" : "csv/practica_fase2";
        string pf = (fase == 1) ? "f1" : "f2";
        var rows = ParseCsv(csv);
        if (rows == null || rows.Count == 0) { Debug.LogWarning("Práctica sin filas: " + csv); yield break; }
        var row = rows[0];

        var trial = NewTrial(row, fase, 0, true);
        EyeTrackingLogger.Instance.ClearSamples();
        EyeTrackingLogger.Instance.trackingActive = true;

        // Parte A — codificación
        yield return StartCoroutine(ShowInstr($"instr_practica_{pf}", "Practica - Parte A"));
        yield return StartCoroutine(RunSteps(BuildStageA(row, fase), trial));
        // Parte B — interferencia
        yield return StartCoroutine(ShowInstr($"instr_p{pf}_parteB", "Practica - Parte B"));
        yield return StartCoroutine(RunSteps(BuildStageB(row, fase), trial));
        // Parte C — reconocimiento / decisión
        yield return StartCoroutine(ShowInstr($"instr_p{pf}_parteC", "Practica - Parte C"));
        yield return StartCoroutine(RunSteps(BuildStageC(row, fase), trial));

        EyeTrackingLogger.Instance.trackingActive = false;
        trial.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        trial.gazeSamples = EyeTrackingLogger.Instance.EndAndReturnSamples();
        trials.Add(trial);
        Debug.Log($"Practica guiada F{fase} guardada. Respuestas={trial.responses.Count}");

        // segunda práctica completa (si hay 2da fila en el CSV)
        if (rows.Count > 1)
        {
            yield return StartCoroutine(ShowInstr($"instr_p{pf}_segunda", "Segunda practica (completa)"));
            yield return StartCoroutine(RunTrial(rows[1], fase, 0, true));
        }
    }

    // =====================================================================
    //  CONSTRUCCIÓN DE PASOS (tiempos exactos del .osexp web — ver ESPEC §1/§2)
    // =====================================================================
    // Stage A (codificación) — idéntica en F1 y F2: fix 1000 + (img 1250 + fix 500) x4 = 8000 ms
    private List<TrialStep> BuildStageA(Dictionary<string, string> row, int fase)
    {
        var s = new List<TrialStep>();
        string p = (fase == 1) ? "f1" : "f2";
        s.Add(TrialStep.Fixation($"{p}_fix", 1000f));
        for (int i = 1; i <= 4; i++)
        {
            s.Add(TrialStep.Image($"{p}_stageA_{i}", Img(Get(row, $"stageA_img_{i}")), 1250f));
            s.Add(TrialStep.Fixation($"{p}_fix", 500f));
        }
        return s;
    }

    // Stage B (interferencia): F1 = 4 aritméticas (4s); F2 = 2 razonamientos V/F (10s)
    private List<TrialStep> BuildStageB(Dictionary<string, string> row, int fase)
    {
        var s = new List<TrialStep>();
        if (fase == 1)
        {
            for (int i = 1; i <= 4; i++)
                s.Add(TrialStep.Response($"f1_stageB_{i}", Img(Get(row, $"stageB_img_{i}")),
                    4000f, Get(row, $"b_correct_{i}"), $"b{i}", Get(row, $"b_op_{i}")));
        }
        else
        {
            s.Add(TrialStep.Response("f2_stageB_1", Img(Get(row, "stageB_img_1")),
                10000f, Get(row, "correct_b1"), "b1", Get(row, "b1_acr1") + "," + Get(row, "b1_acr2")));
            s.Add(TrialStep.Response("f2_stageB_2", Img(Get(row, "stageB_img_2")),
                10000f, Get(row, "correct_b2"), "b2", Get(row, "b2_acr1") + "," + Get(row, "b2_acr2")));
        }
        return s;
    }

    // Stage C: F1 = 4 reconocimientos (3s); F2 = decisión P (3.5s) + Q (3.5s) + PQ (respuesta 10s)
    private List<TrialStep> BuildStageC(Dictionary<string, string> row, int fase)
    {
        var s = new List<TrialStep>();
        if (fase == 1)
        {
            for (int i = 1; i <= 4; i++)
                s.Add(TrialStep.Response($"f1_stageC_{i}", Img(Get(row, $"stageC_img_{i}")),
                    3000f, Get(row, $"c_correct_{i}"), $"c{i}",
                    Get(row, $"c_letter_{i}") + "@" + Get(row, $"c_quad_{i}")));
        }
        else
        {
            s.Add(TrialStep.Image("decision_P", Img(Get(row, "stageC_img_P")), 3500f));
            s.Add(TrialStep.Image("decision_Q", Img(Get(row, "stageC_img_Q")), 3500f));
            s.Add(TrialStep.Response("decision_PQ", Img(Get(row, "stageC_img_PQ")),
                10000f, null, "decision", Get(row, "period_id")));
        }
        return s;
    }

    // =====================================================================
    //  RESPUESTA  (input de Victoria SIN CAMBIOS: PChosen/QChosen los llaman los botones)
    // =====================================================================
    public void PChosen() { RegisterKey("p"); }
    public void QChosen() { RegisterKey("q"); }

    private void RegisterKey(string k)
    {
        if (!awaitingResponse) return;            // ignora clicks fuera de ventana de respuesta
        responseKey = k;
        responseTimeRealtime = Time.realtimeSinceStartupAsDouble;
    }

    private IEnumerator WaitForResponse(float timeoutMs)
    {
        awaitingResponse = true;
        responseKey = null;
        responseTimeRealtime = -1;
        double onset = Time.realtimeSinceStartupAsDouble;
        float elapsed = 0f;

        while (responseKey == null && elapsed < timeoutMs / 1000f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        awaitingResponse = false;
        lastKey = responseKey; // null si TIMEOUT
        lastOnsetRealtime = onset;
        lastRtMs = (responseKey != null) ? (float)((responseTimeRealtime - onset) * 1000.0) : -1f;
    }

    private ResponseRecordASG BuildRecord(TrialStep step, string key, float rtMs, double onsetRealtime)
    {
        double onsetMs = (onsetRealtime - expStartRealtime) * 1000.0;
        var rec = new ResponseRecordASG
        {
            stage = step.stage,
            field = step.field,
            prompt = step.prompt,
            rt_ms = rtMs,
            t_onset_ms = onsetMs,
            t_response_ms = (rtMs >= 0) ? onsetMs + rtMs : -1
        };

        if (key == null) // TIMEOUT
        {
            rec.key = "TIMEOUT"; rec.meaning = "TIMEOUT";
            rec.correct_answer = step.correctAnswer ?? "NA";
            rec.is_correct = (step.field == "decision") ? -9 : -1;
        }
        else if (step.field == "decision")
        {
            rec.key = key; rec.meaning = (key == "p") ? "P" : "Q";
            rec.correct_answer = "NA"; rec.is_correct = -9; // decisión no tiene "correcta"
        }
        else // Stage B / C : V/F
        {
            rec.key = key; rec.meaning = (key == "p") ? "True" : "False";
            rec.correct_answer = step.correctAnswer;
            rec.is_correct = (rec.meaning == step.correctAnswer) ? 1 : 0;
        }
        return rec;
    }

    // =====================================================================
    //  CALIBRACIÓN  (reusada VERBATIM del manager de Victoria; waitStart=false para recalibrar)
    // =====================================================================
    public void StartCalibration()
    {
        startClicked = true;
        if (calibrationInstructionsPanel != null) calibrationInstructionsPanel.SetActive(false);
    }

    // Construye los targets sobre el plano-imagen.
    // 9 = grilla 3x3 (completa) ; 5 = centro + 4 esquinas (rapida) ; 16 = rejilla 4x4 original de Victoria
    private List<Vector3> BuildGrid(int pointCount)
    {
        Vector3 imageCenter = canvasCenter + imageOffset;
        float hx = imageWidth / 2f;
        float hy = imageHeight / 2f;
        var pts = new List<Vector3>();

        if (pointCount == 5)
        {
            pts.Add(imageCenter);                              // centro
            pts.Add(imageCenter + new Vector3(-hx,  hy, 0));   // sup izq
            pts.Add(imageCenter + new Vector3( hx,  hy, 0));   // sup der
            pts.Add(imageCenter + new Vector3(-hx, -hy, 0));   // inf izq
            pts.Add(imageCenter + new Vector3( hx, -hy, 0));   // inf der
        }
        else if (pointCount == 16)
        {
            int div = 4;
            float stepX = imageWidth / (div - 1);
            float stepY = imageHeight / (div - 1);
            for (int ix = 0; ix < div; ix++)
                for (int iy = 0; iy < div; iy++)
                {
                    float x = -hx + ix * stepX;
                    float y = -hy + iy * stepY;
                    pts.Add(imageCenter + new Vector3(x, y, 0));
                }
        }
        else // 9 -> grilla 3x3
        {
            float[] xs = { -hx, 0f, hx };
            float[] ys = {  hy, 0f, -hy };
            foreach (float y in ys)
                foreach (float x in xs)
                    pts.Add(imageCenter + new Vector3(x, y, 0));
        }
        return pts;
    }

    // Una calibracion (inicial o recal). Guarda el CRUDO + el targetPosition por punto.
    // La tecnologia de captura es la de Victoria; solo cambia que ahora se repite y se guarda con etiqueta.
    private IEnumerator Calibrate(bool waitStart, string label, int pointCount, int afterTrial)
    {
        // ocultar estímulo/fixation/botones durante la calibración (importa en recalibraciones)
        if (imageComponent != null) imageComponent.enabled = false;
        if (fixationCross != null) fixationCross.SetActive(false);
        SetButtons(false);

        if (waitStart)
        {
            startClicked = false;
            if (calibrationInstructionsPanel != null) calibrationInstructionsPanel.SetActive(true);
            yield return new WaitUntil(() => startClicked);
        }

        var segment = new CalibrationSegment
        {
            label = label,
            type = (pointCount == 5) ? "quick_5" : (pointCount == 16 ? "full_16" : "full_9"),
            afterTrial = afterTrial,
            points = new List<CalibrationPoint>()
        };

        List<Vector3> targetPositions = BuildGrid(pointCount);

        int target_index = 1;
        foreach (Vector3 target in targetPositions)
        {
            EyeTrackingLogger.Instance.trackingActive = true;
            EyeTrackingLogger.Instance.currentStage = $"{label}_target_{target_index}";

            yield return StartCoroutine(ShowCalibrationSphere(target));

            var raw = EyeTrackingLogger.Instance.EndAndReturnSamples();
            EyeTrackingLogger.Instance.trackingActive = false;

            segment.points.Add(new CalibrationPoint
            {
                targetIndex = target_index,
                targetPosition = target,   // el target real queda guardado
                gazeSamples = raw          // crudo completo, sin promediar
            });

            target_index++;
            yield return new WaitForSeconds(0.5f);
        }

        calibrations.Add(segment);
        Debug.Log($"Calibracion '{label}' guardada: {segment.points.Count} puntos.");
    }

    private IEnumerator ShowCalibrationSphere(Vector3 position, float duration = 5f)
    {
        GameObject sphere = Instantiate(calibrationSphere, position, Quaternion.identity);
        sphere.SetActive(true);
        yield return new WaitForSeconds(0.5f);
        EyeTrackingLogger.Instance.ClearSamples();
        yield return new WaitForSeconds(duration - 1f);
        Destroy(sphere);
        yield return new WaitForSeconds(0.8f);
    }

    // =====================================================================
    //  HELPERS UI
    // =====================================================================
    private void ShowInstruction(string text)
    {
        if (textActivity != null) textActivity.text = text;
        if (panelMessage != null) panelMessage.SetActive(true);
        if (activityPanel != null) activityPanel.SetActive(false);
        SetButtons(false);
        if (fixationCross != null) fixationCross.SetActive(false);
    }

    // continueButton.onClick -> este método
    public void ContinueActivity()
    {
        if (panelMessage != null) panelMessage.SetActive(false);
        if (activityPanel != null) activityPanel.SetActive(true);
        continuePressed = true;
    }

    private void ShowImage(Sprite s)
    {
        if (fixationCross != null) fixationCross.SetActive(false);
        if (imageComponent != null)
        {
            imageComponent.enabled = true;
            if (s != null) imageComponent.sprite = s;
        }
    }

    private void ShowFixation()
    {
        if (imageComponent != null) imageComponent.enabled = false;
        if (fixationCross != null) fixationCross.SetActive(true);
        SetButtons(false);
    }

    private void SetButtons(bool on)
    {
        if (buttonP != null) buttonP.SetActive(on);
        if (buttonQ != null) buttonQ.SetActive(on);
    }

    // =====================================================================
    //  CSV / SPRITES
    // =====================================================================
    private List<Dictionary<string, string>> ParseCsv(string resourcePath)
    {
        TextAsset ta = Resources.Load<TextAsset>(resourcePath);
        if (ta == null) { Debug.LogError("No se encontró TextAsset: " + resourcePath); return null; }

        var rows = new List<Dictionary<string, string>>();
        string[] lines = ta.text.Replace("\r", "").Split('\n');
        if (lines.Length < 2) return rows;

        string[] header = lines[0].Split(',');
        for (int li = 1; li < lines.Length; li++)
        {
            if (string.IsNullOrWhiteSpace(lines[li])) continue;
            string[] cells = lines[li].Split(','); // los campos no contienen comas (verificado)
            var dict = new Dictionary<string, string>();
            for (int c = 0; c < header.Length && c < cells.Length; c++)
                dict[header[c].Trim()] = cells[c].Trim();
            rows.Add(dict);
        }
        return rows;
    }

    private static string Get(Dictionary<string, string> row, string key)
    {
        return (row != null && row.TryGetValue(key, out var v)) ? v : "";
    }

    private Sprite Img(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        string key = "pool/" + Path.GetFileNameWithoutExtension(fileName);
        if (spriteCache.TryGetValue(key, out var cached)) return cached;
        Sprite s = Resources.Load<Sprite>(key);
        if (s == null) Debug.LogWarning("Sprite no encontrado en Resources: " + key);
        spriteCache[key] = s;
        return s;
    }

    // =====================================================================
    //  GUARDADO  (mismo enfoque local de Victoria; envío a server queda comentado)
    // =====================================================================
    public void TerminarExperimento()
    {
        SaveResults();
        Application.Quit();
    }

    private bool resultsSaved = false;
    private void SaveResults()
    {
        if (resultsSaved) return;   // evita guardar dos veces (auto + botón Terminar)
        resultsSaved = true;
        meta.timestamp_end = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        meta.duration_total_ms = (Time.realtimeSinceStartupAsDouble - expStartRealtime) * 1000.0;

        var data = new ExperimentDataASG(meta, calibrations, trials);
        string json = JsonUtility.ToJson(data, false);

        string fileName = $"ResultadosASG_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string dir = Application.persistentDataPath;
#if UNITY_EDITOR
        // en el editor: guardar en Descargas para encontrarlo fácil; en el visor real se usa persistentDataPath
        string downloads = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) dir = downloads;
#endif
        string filePath = Path.Combine(dir, fileName);
        File.WriteAllText(filePath, json);
        Debug.Log("Resultados ASG guardados en: " + filePath);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(filePath); // abre el explorador en el archivo (solo en el editor)
#endif
    }
}

// ============================================================================
//  ESTRUCTURAS
// ============================================================================
public class TrialStep
{
    public string stage;        // etiqueta currentStage
    public Sprite sprite;       // imagen a mostrar (null si fixation)
    public bool isFixation;
    public float durationMs;    // duración fija (si no espera respuesta)
    public bool waitsResponse;
    public float timeoutMs;
    public string correctAnswer; // "True"/"False" o null (decisión)
    public string field;         // "b1","c3","decision",...
    public string prompt;        // texto fuente del CSV (para verificación/log)

    public static TrialStep Fixation(string stage, float durationMs)
        => new TrialStep { stage = stage, isFixation = true, durationMs = durationMs };

    public static TrialStep Image(string stage, Sprite sprite, float durationMs)
        => new TrialStep { stage = stage, sprite = sprite, durationMs = durationMs };

    public static TrialStep Response(string stage, Sprite sprite, float timeoutMs,
                                     string correctAnswer, string field, string prompt)
        => new TrialStep
        {
            stage = stage,
            sprite = sprite,
            waitsResponse = true,
            timeoutMs = timeoutMs,
            correctAnswer = correctAnswer,
            field = field,
            prompt = prompt
        };
}

[Serializable]
public class ResponseRecordASG
{
    public string stage;
    public string field;
    public string prompt;
    public string key;            // "p"/"q"/"TIMEOUT"
    public string meaning;        // "True"/"False"/"P"/"Q"/"TIMEOUT"
    public float rt_ms;           // -1 si TIMEOUT
    public string correct_answer; // "True"/"False"/"NA"
    public int is_correct;        // 1 / 0 / -1 (timeout V/F) / -9 (decisión, N/A)
    public double t_onset_ms;
    public double t_response_ms;
}

[Serializable]
public class TrialResultsASG
{
    public string trial;
    public int fase;
    public int block;
    public bool is_practice;
    public string period_id;
    public string timestamp;
    public List<ResponseRecordASG> responses;
    public string decision_key;
    public string decision_choice;
    public float decision_rt_ms;
    public List<GazeSample> gazeSamples;
}

[Serializable]
public class ExperimentMetaASG
{
    public string experiment_version;
    public string timestamp_start;
    public string timestamp_end;
    public double duration_total_ms;
    public Vector3 buttonP_world;  // posición de los botones (hipótesis input, ESPEC §6.b)
    public Vector3 buttonQ_world;
}

[Serializable]
public class ExperimentDataASG
{
    public ExperimentMetaASG meta;
    public List<CalibrationSegment> calibrations;   // inicial + 5 recalibraciones (cada punto con target + crudo)
    public List<TrialResultsASG> trials;

    public ExperimentDataASG(ExperimentMetaASG meta, List<CalibrationSegment> calibrations, List<TrialResultsASG> trials)
    {
        this.meta = meta;
        this.calibrations = calibrations;
        this.trials = trials;
    }
}