using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;


[System.Serializable]
public class StatueEntry
{
    public StatueData data;
    public Transform targetPoint;
    public CinemachineCamera statueCam;
}

public class TourManager : MonoBehaviour
{
    public NavMeshAgent agent;
    public Animator animator;
    private GeminiManager gemini;

    [Header("UI Panels")]
    public GameObject startPanel;
    public GameObject endPanel;
    public GameObject loadingScreen;

    [Header("Cameras")]
    public CinemachineCamera followCam;
    public List<StatueEntry> statueList = new List<StatueEntry>();

    private int currentIndex = 0;
    private bool isArrived = false;
    private bool canCheckArrival = false;
    public bool tourStarted = false;

    void Start()
    {
        // 1. إظهار لوحة البداية فقط
        if (startPanel != null) startPanel.SetActive(true);
        if (endPanel != null) endPanel.SetActive(false);

        tourStarted = false;

        gemini = GetComponent<GeminiManager>();
        ResetAllCameras();
        if (followCam != null) followCam.Priority = 20;

        if (agent != null) agent.stoppingDistance = 0.1f;

        // تأكد أن العميل متوقف في البداية تماماً
        if (agent != null) agent.isStopped = true;
    }

    void ResetAllCameras()
    {
        foreach (var entry in statueList)
            if (entry.statueCam != null) entry.statueCam.Priority = 10;
    }

    // الدالة الأصلية للتحريك (تبقي كما هي ولكن تُستدعى بعد التحية)
    public void StartTour()
    {
        tourStarted = true;
        currentIndex = 0;
        GoToStatue();
    }

    // --- الجزء الجديد: تسلسل الترحيب قبل الحركة ---
    IEnumerator InitialGreetingSequence()
    {
        // 1. لف حورس ليبص للكاميرا/اللاعب قبل الكلام
        yield return StartCoroutine(FacePlayer());

        // 2. تشغيل حركة الترحيب (اللوحة)
        if (animator != null) animator.SetTrigger("Wave");

        yield return new WaitForSeconds(3.0f);
        // 2. جملة التحية الافتتاحية (تعدلها كما تحب)
        yield return StartCoroutine(gemini.SpeakAndType("أهلاً بك يا صديقي! أنا حورس، مرشدك السياحي. جاهز نبدأ الجولة؟"));

        // 4. تفعيل وضع الجولة وتصفير العداد لكن "بدون" أمر حركة
        tourStarted = true;
        currentIndex = 0;
        isArrived = false;
        canCheckArrival = false;

        Debug.Log("حورس مستعد، في انتظار أمر التحرك (يلا بينا أو Ctrl)");

        // 3. الآن فقط تبدأ الجولة الفعلية والحركة لأول تمثال
        //StartTour();
    }

    // تعديل زرار البداية
    public void OnClickStartTour()
    {
        if (startPanel != null) startPanel.SetActive(false);
        // بدل ما نبدأ الجولة فوراً، هنبدأ كوروتين التحية
        StartCoroutine(InitialGreetingSequence());
    }

    void Update()
    {
        if (agent == null) return;

        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            HandleNavigationInput();
        }

        if (agent.velocity.magnitude < 0.1f)
        {
            LookAtCamera();
        }

        if (!tourStarted) return;
        UpdateMovementLogic();
    }

    public void HandleNavigationInput()
    {
        // 1. فحص لو حورس مشغول (بيتكلم أو بيكتب)
        if (gemini != null && gemini.IsCharacterBusy())
        {
            Debug.LogWarning("التحرك مرفوض: حورس لسه بيتكلم.");
            return;
        }

        // 2. حالة البداية: لو لسه في مرحلة الترحيب ومتحركناش
        if (!tourStarted || (currentIndex == 0 && !isArrived && agent.velocity.magnitude < 0.1f && !canCheckArrival))
        {
            Debug.Log("بدء التحرك لأول تمثال...");
            tourStarted = true;
            GoToStatue();
            return;
        }

        // 3. المنطق الطبيعي للانتقال بين التماثيل
        float dist = Vector3.Distance(transform.position, statueList[currentIndex].targetPoint.position);
        if (isArrived || dist <= agent.stoppingDistance + 0.5f)
        {
            MoveToNext();
        }
        else
        {
            Debug.LogWarning("التحرك مرفوض: حورس لسه موصلش للمكان الحالي.");
        }
    }

    void UpdateMovementLogic()
    {
        animator.SetFloat("Speed", agent.velocity.magnitude);

        if (agent.velocity.magnitude > 0.1f)
        {
            Quaternion lookRot = Quaternion.LookRotation(agent.velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
        }
        else if (isArrived || agent.isStopped)
        {
            LookAtCamera();
        }

        if (canCheckArrival && !isArrived && !agent.pathPending)
        {
            float dist = Vector3.Distance(transform.position, statueList[currentIndex].targetPoint.position);
            if (agent.remainingDistance <= agent.stoppingDistance || dist <= agent.stoppingDistance)
            {
                isArrived = true;
                canCheckArrival = false;
                StartCoroutine(TriggerStatueInfo());
            }
        }
    }

    void GoToStatue()
    {
        if (currentIndex < statueList.Count)
        {
            StopAllCoroutines();
            isArrived = false;
            canCheckArrival = false;
            agent.isStopped = false;
            ResetAllCameras();
            if (followCam != null) followCam.Priority = 20;

            agent.SetDestination(statueList[currentIndex].targetPoint.position);
            StartCoroutine(MovementCooldown());
        }
    }

    IEnumerator MovementCooldown()
    {
        yield return new WaitForSeconds(1f);
        canCheckArrival = true;
    }

    IEnumerator TriggerStatueInfo()
    {
        agent.isStopped = true;
        if (statueList[currentIndex].statueCam != null)
            statueList[currentIndex].statueCam.Priority = 30;

        yield return StartCoroutine(FacePlayer());

        StatueData currentData = statueList[currentIndex].data;
        gemini.currentStatueContext = currentData.info;

        string speech = $"إحنا دلوقتي قدام {currentData.statueName}. {currentData.info}";
        yield return StartCoroutine(gemini.SpeakAndType(speech, true, currentData.preRecordedAudio));

        yield return StartCoroutine(gemini.SpeakAndType("لو حابب تعرف أكتر عن التمثال ده أنا موجود، أو قولي 'يلا بينا' عشان نشوف اللي بعده"));
    }

    public void MoveToNext()
    {
        currentIndex++;
        if (currentIndex < statueList.Count)
        {
            GoToStatue();
        }
        else
        {
            StartCoroutine(EndTourSequence());
        }
    }

    IEnumerator EndTourSequence()
    {
        ResetAllCameras();
        if (followCam != null) followCam.Priority = 20;

        yield return StartCoroutine(FacePlayer());

        yield return StartCoroutine(gemini.SpeakAndType("لحد هنا وجولتنا خلصت انهارده أتمني تكون استمتعت. نورتني!"));

        tourStarted = false;
        isArrived = false;

        if (endPanel != null) endPanel.SetActive(true);
    }

    void LookAtCamera()
    {
        if (Camera.main == null) return;
        Vector3 direction = Camera.main.transform.position - transform.position;
        direction.y = 0;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 3f);
        }
    }

    IEnumerator FacePlayer()
    {
        if (Camera.main == null) yield break;
        float duration = 1.5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            Vector3 direction = Camera.main.transform.position - transform.position;
            direction.y = 0;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, elapsed / duration);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }





    public void RestartTour()
    {
        StartCoroutine(LoadAsynchronously());
    }

    IEnumerator LoadAsynchronously()
    {
        // 1. نبدأ تحميل المشهد في الخلفية
        AsyncOperation operation = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().name);

        // 2. نظهر شاشة التحميل (البردي أو اللوحة اللي صممتها)
        if (loadingScreen != null)
            loadingScreen.SetActive(true);

        // 3. الجزء اللي كان ناقص: حلقة تكرار بتخلي الكود "يستنى" طول ما التحميل لسه مخلصش
        while (!operation.isDone)
        {
            // هنا ممكن تحسب نسبة التحميل لو عندك ProgressBar
            // float progress = Mathf.Clamp01(operation.progress / 0.9f);

            yield return null; // استنى للفريم الجاي وكرر التشييك
        }
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}