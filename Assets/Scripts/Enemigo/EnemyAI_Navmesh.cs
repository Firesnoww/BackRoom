using UnityEngine;
using UnityEngine.AI;

public class EnemyAI_Navmesh : MonoBehaviour
{
    public enum State { Patrol, Chase, Search }

    [Header("Refs")]
    public Transform player;                 // Arrastra el Player (o se intenta encontrar por tag "Player")
    public NavMeshAgent agent;

    [Header("Patrol")]
    public float patrolRadius = 12f;         // radio para puntos aleatorios
    public float patrolSpeed = 2.2f;
    public float waypointPause = 0.5f;       // pausa mínima al llegar (sensación natural)

    [Header("Chase")]
    public float chaseSpeed = 3.8f;
    public float visionRange = 14f;          // distancia máxima para ver
    [Range(0f, 180f)] public float visionAngle = 95f; // FOV (en grados)
    public float eyeHeight = 1.6f;           // altura “ojos” del enemigo
    public float playerHeadOffset = 1.5f;    // altura aproximada de la cabeza del jugador

    [Header("Perdida de vista / Búsqueda")]
    public float loseSightTime = 2.0f;       // cuánto “recuerda” después de perder línea de visión
    public float searchDuration = 5.0f;      // cuánto dura la búsqueda
    public float searchRadius = 5.0f;        // radio de puntos aleatorios alrededor de la última vista

    [Header("Raycast / Visión")]
    [Tooltip("Capas que bloquean la visión (ej. Walls, Props macizos). SOLO estas capas se consideran como obstrucciones.")]
    public LayerMask obstructionMask = ~0;
    [Tooltip("Ignorar colliders con isTrigger en la comprobación de línea de visión.")]
    public bool ignoreTriggers = true;

    // Estado
    public State state = State.Patrol;
    private Vector3 homePos;
    private Vector3 lastKnownPlayerPos;
    private float timeSinceLastSeen = Mathf.Infinity;
    private float searchTimer = 0f;
    private float arriveTimer = 0f;

    // Depuración LOS
    private Vector3 _dbgEye, _dbgHead;
    private bool _dbgHasLOS;

    void OnValidate()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }

        if (obstructionMask == 0)
        {
            Debug.LogWarning($"[{name}] 'obstructionMask' está vacío. El enemigo podría ver a través de todo. Marca tu capa 'Walls' aquí.");
        }
    }

    void Reset()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }
    }

    void Start()
    {
        homePos = transform.position;
        GotoRandomPatrolPoint();
        SetAgentSpeed(patrolSpeed);
    }

    void Update()
    {
        if (!agent || !agent.isOnNavMesh || player == null) return;

        // Chequeo de visión en todos los estados
        bool seesPlayer = CanSeePlayer();

        switch (state)
        {
            case State.Patrol:
                PatrolTick(seesPlayer);
                break;

            case State.Chase:
                ChaseTick(seesPlayer);
                break;

            case State.Search:
                SearchTick(seesPlayer);
                break;
        }
    }

    // ---------- Estados ----------
    void PatrolTick(bool seesPlayer)
    {
        SetAgentSpeed(patrolSpeed);

        if (seesPlayer)
        {
            state = State.Chase;
            timeSinceLastSeen = 0f;
            lastKnownPlayerPos = player.position;
            return;
        }

        // Si llegó (o casi), pequeña “pausa” y nuevo destino
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            arriveTimer += Time.deltaTime;
            if (arriveTimer >= waypointPause)
            {
                arriveTimer = 0f;
                GotoRandomPatrolPoint();
            }
        }
    }

    void ChaseTick(bool seesPlayer)
    {
        SetAgentSpeed(chaseSpeed);

        if (seesPlayer)
        {
            lastKnownPlayerPos = player.position;
            timeSinceLastSeen = 0f;
        }
        else
        {
            timeSinceLastSeen += Time.deltaTime;
        }

        // Perseguir siempre hacia la última posición conocida/actual
        agent.SetDestination(lastKnownPlayerPos);

        // Si lo “perdió” por X segundos → Search
        if (timeSinceLastSeen >= loseSightTime)
        {
            state = State.Search;
            searchTimer = 0f;
            // Al entrar a Search, ve a la última posición conocida
            agent.SetDestination(lastKnownPlayerPos);
        }
    }

    void SearchTick(bool seesPlayer)
    {
        SetAgentSpeed(patrolSpeed * 1.1f); // camina un poco más rápido que patrulla
        searchTimer += Time.deltaTime;

        if (seesPlayer)
        {
            state = State.Chase;
            timeSinceLastSeen = 0f;
            lastKnownPlayerPos = player.position;
            return;
        }

        // Si llegó al punto de búsqueda, elige otro aleatorio alrededor
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            Vector3 p = RandomPointOnNavmesh(lastKnownPlayerPos, searchRadius);
            agent.SetDestination(p);
        }

        // Se acabó el tiempo de búsqueda → volver a patrullar
        if (searchTimer >= searchDuration)
        {
            state = State.Patrol;
            GotoRandomPatrolPoint();
        }
    }

    // ---------- Utilidades ----------
    bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 head = player.position + Vector3.up * playerHeadOffset;
        Vector3 toPlayer = head - eye;

        float dist = toPlayer.magnitude;
        if (dist > visionRange)
        {
            _dbg(eye, head, false);
            return false;
        }

        // FOV (en planta)
        Vector3 flatForward = transform.forward; flatForward.y = 0f; flatForward.Normalize();
        Vector3 flatToPlayer = toPlayer; flatToPlayer.y = 0f; flatToPlayer.Normalize();
        float halfFOV = visionAngle * 0.5f;
        if (Vector3.Angle(flatForward, flatToPlayer) > halfFOV)
        {
            _dbg(eye, head, false);
            return false;
        }

        // Línea de visión: SOLO chequea capas de 'obstructionMask'
        // Si golpea algo en ese mask, significa que hay una pared/obstrucción entre ambos
        QueryTriggerInteraction qti = ignoreTriggers ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;

        bool blocked = Physics.Linecast(eye, head, out RaycastHit hit, obstructionMask, qti);
        if (blocked)
        {
            // Hay obstrucción en las capas indicadas -> no ve al jugador
            _dbg(eye, hit.point, false);
            return false;
        }

        // No hay obstrucción en esas capas -> línea de visión clara
        _dbg(eye, head, true);
        return true;
    }

    void _dbg(Vector3 eye, Vector3 target, bool hasLOS)
    {
        _dbgEye = eye; _dbgHead = target; _dbgHasLOS = hasLOS;
        Debug.DrawLine(eye, target, hasLOS ? Color.green : Color.magenta);
    }

    void GotoRandomPatrolPoint()
    {
        Vector3 p = RandomPointOnNavmesh(homePos, patrolRadius);
        agent.SetDestination(p);
    }

    Vector3 RandomPointOnNavmesh(Vector3 center, float radius)
    {
        for (int i = 0; i < 20; i++)
        {
            Vector3 rand = center + Random.insideUnitSphere * radius;
            if (NavMesh.SamplePosition(rand, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                return hit.position;
        }
        // Fallback: posición actual
        return transform.position;
    }

    void SetAgentSpeed(float s)
    {
        if (!agent) return;
        agent.speed = s;
        agent.acceleration = Mathf.Max(8f, s * 3f);
        agent.angularSpeed = 720f; // reacción ágil
        agent.stoppingDistance = 0.2f;
        agent.updateRotation = true;
        agent.updateUpAxis = true;
    }

    // Gizmos para depurar
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, patrolRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, visionRange);

        // Cono de visión (aprox)
        Vector3 f = transform.forward;
        Quaternion left = Quaternion.AngleAxis(-visionAngle * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(visionAngle * 0.5f, Vector3.up);
        Gizmos.color = new Color(1, 0, 0, 0.4f);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, left * f * visionRange);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, right * f * visionRange);

        // Línea de visión última calculada
        Gizmos.color = _dbgHasLOS ? Color.green : Color.magenta;
        Gizmos.DrawLine(_dbgEye, _dbgHead);
    }
}
