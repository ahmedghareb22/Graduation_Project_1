using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

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

    [Header("Cameras")]
    public CinemachineCamera followCam;
    public List<StatueEntry> statueList = new List<StatueEntry>();

    private int currentIndex = 0;
    private bool isArrived = false;
    private bool canCheckArrival = false;
    public bool tourStarted = false;

    void Start()
    {
        gemini = GetComponent<GeminiManager>();
        ResetAllCameras();
        if (followCam != null) followCam.Priority = 20;

        // خلي الـ Stopping Distance كبير شوية (2 متر مثلاً) عشان نضمن الوصول
        if (agent != null) agent.stoppingDistance = 0.5f;
    }

    void ResetAllCameras()
    {
        foreach (var entry in statueList)
            if (entry.statueCam != null) entry.statueCam.Priority = 10;
    }

    public void StartTour()
    {
        tourStarted = true;
        currentIndex = 0;
        GoToStatue();
    }

    void Update()
    {
        if (agent == null) return;

        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            HandleNavigationInput();
        }
        // التعديل هنا: لو مش بيتحرك، خليه يبص للكاميرا دايماً حتى لو الجولة خلصت
        if (agent.velocity.magnitude < 0.1f)
        {
            LookAtCamera();
        }

        if (!tourStarted) return;
        UpdateMovementLogic();
    }

    public void HandleNavigationInput()
    {
        if (gemini != null && gemini.IsCharacterBusy())
        {
            Debug.LogWarning("التحرك مرفوض: حورس لسه بيتكلم.");
            return;
        }

        if (!tourStarted) { StartTour(); return; }

        // لو ضغطت "يلا" وهو واقف فعلياً بس الكود معلق، هنساعده هنا
        float dist = Vector3.Distance(transform.position, statueList[currentIndex].targetPoint.position);
        if (isArrived || dist <= agent.stoppingDistance + 0.5f)
        {
            MoveToNext();
        }
        else
        {
            Debug.LogWarning("التحرك مرفوض: حورس لسه موصلش للمكان.");
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
            // نداء دالة البص للكاميرا اللي طلبتها
            LookAtCamera();
        }

        if (canCheckArrival && !isArrived && !agent.pathPending)
        {
            // تشييك إضافي بالمسافة المباشرة (أضمن من remainingDistance)
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
            // بدل ما نقفل الجولة فوراً، هنشغل كوروتين النهاية
            StartCoroutine(EndTourSequence());
        }
    }

    IEnumerator EndTourSequence()
    {
        Debug.Log("جاري إنهاء الجولة...");

        // 1. نرجع الكاميرا الأساسية
        ResetAllCameras();
        if (followCam != null) followCam.Priority = 20;

        // 2. يلف يبص لك الأول قبل ما يتكلم
        yield return StartCoroutine(FacePlayer());

        // 3. يقول جملة النهاية وهو باصص لك
        yield return StartCoroutine(gemini.SpeakAndType("لحد هنا وجولتنا خلصت انهارده أتمني تكون استمتعت. نورتني!"));

        // 4. نقفل الجولة رسمياً
        tourStarted = false;
        isArrived = false;
    }


    // --- الدوال اللي رجعتها لك عشان يبص للكاميرا ---

    void LookAtCamera()
    {
        if (Camera.main == null) return;
        Vector3 direction = Camera.main.transform.position - transform.position;
        direction.y = 0; // عشان ميميلش بجسمه
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
}