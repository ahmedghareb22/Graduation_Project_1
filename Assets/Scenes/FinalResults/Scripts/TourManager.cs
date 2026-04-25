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
    private bool canCheckArrival = false; // لمنع الكاميرا من التبديل قبل الحركة
    public bool tourStarted = false;

    void Start()
    {
        gemini = GetComponent<GeminiManager>();
        ResetAllCameras();
        if (followCam != null) followCam.Priority = 20;
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
        if (agent == null || !tourStarted) return;

        animator.SetFloat("Speed", agent.velocity.magnitude);

        // التحكم في الدوران أثناء المشي
        if (agent.velocity.magnitude > 0.1f)
        {
            Quaternion lookRot = Quaternion.LookRotation(agent.velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
        }
        else if (isArrived) { LookAtCamera(); }

        // التحقق من الوصول (فقط إذا كان مسموحاً لنا بالتحقق)
        if (canCheckArrival && !isArrived && !agent.pathPending)
        {
            if (agent.remainingDistance <= agent.stoppingDistance)
            {
                isArrived = true;
                canCheckArrival = false; // نوقف التحقق عشان ميتكررش
                StartCoroutine(TriggerStatueInfo());
            }
        }
    }

    void GoToStatue()
    {
        if (currentIndex < statueList.Count)
        {
            StopAllCoroutines(); // نوقف أي كلام أو حركات سابقة
            isArrived = false;
            canCheckArrival = false;

            agent.isStopped = false;
            ResetAllCameras();
            if (followCam != null) followCam.Priority = 20;

            agent.SetDestination(statueList[currentIndex].targetPoint.position);

            // ننتظر نصف ثانية قبل السماح بالتحقق من الوصول
            StartCoroutine(MovementCooldown());
        }
    }

    IEnumerator MovementCooldown()
    {
        yield return new WaitForSeconds(0.5f);
        canCheckArrival = true;
    }

    IEnumerator TriggerStatueInfo()
    {
        agent.isStopped = true;

        // تفعيل كاميرا التمثال
        if (statueList[currentIndex].statueCam != null)
            statueList[currentIndex].statueCam.Priority = 30;

        yield return StartCoroutine(FacePlayer());

        if (currentIndex < statueList.Count)
        {
            StatueData currentData = statueList[currentIndex].data;
            gemini.currentStatueContext = currentData.info;

            string speech = $"إحنا دلوقتي قدام {currentData.statueName}. {currentData.info}";
            // نظام الميكس يشتغل هنا
            yield return StartCoroutine(gemini.SpeakAndType(speech, true, currentData.preRecordedAudio));

            yield return StartCoroutine(gemini.SpeakAndType("لو حابب تعرف أكتر عن التمثال ده أنا موجود، أو قولي 'يلا بينا' عشان نشوف اللي بعده"));
        }
    }

    public void MoveToNext()
    {
        if (!isArrived) return; // منع التبديل وهو في الطريق

        currentIndex++;
        if (currentIndex < statueList.Count) GoToStatue();
        else
        {
            ResetAllCameras();
            if (followCam != null) followCam.Priority = 20;
            StartCoroutine(gemini.SpeakAndType("لحد هنا وجولتنا خلصت انهارده أتمني تكون استمتعت. نورتني!"));
            tourStarted = false;
        }
    }

    void LookAtCamera()
    {
        if (Camera.main == null) return;
        Transform target = Camera.main.transform;
        Vector3 dir = (target.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 2f);
    }

    IEnumerator FacePlayer()
    {
        if (Camera.main == null) yield break;
        Transform target = Camera.main.transform;
        float timeout = 2f;
        while (Vector3.Angle(transform.forward, (target.position - transform.position).normalized) > 5f && timeout > 0)
        {
            Vector3 dir = (target.position - transform.position).normalized;
            dir.y = 0;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 180f * Time.deltaTime);
            timeout -= Time.deltaTime;
            yield return null;
        }
    }
}