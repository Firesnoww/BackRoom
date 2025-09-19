using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SimplePlayerController : MonoBehaviour
{
    [Header("Refs")]
    public Transform cam; // Deja vacío y usará Camera.main

    [Header("Movimiento")]
    public float moveSpeed = 4.5f;
    public float gravity = -9.81f;

    [Header("Mouse Look")]
    public float mouseXSens = 250f;   // sensibilidad horizontal
    public float mouseYSens = 200f;   // sensibilidad vertical
    public float minPitch = -50f;     // límite mirar abajo
    public float maxPitch = 65f;      // límite mirar arriba
    public bool lockCursor = true;

    private CharacterController controller;
    private Vector3 velocity;
    private float yaw;   // rotación Y del jugador
    private float pitch; // rotación X de la cámara

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (cam == null && Camera.main != null) cam = Camera.main.transform;

        if (cam == null)
        {
            Debug.LogError("No hay cámara asignada ni Camera.main en escena.");
            enabled = false; return;
        }

        // Inicializar ángulos desde la escena actual
        yaw = transform.eulerAngles.y;
        pitch = cam.localEulerAngles.x;
        // Convertir pitch a rango [-180, 180] para clamping correcto
        if (pitch > 180f) pitch -= 360f;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        MouseLook();
        Move();
    }

    private void MouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseXSens * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseYSens * Time.deltaTime;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Aplicar rotaciones: el cuerpo gira con yaw, la cámara inclina con pitch
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void Move()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v);
        input = Vector3.ClampMagnitude(input, 1f);

        // Mover en el plano XZ relativo a a dónde mira el jugador (yaw)
        Vector3 moveDir = (transform.right * input.x) + (transform.forward * input.z);
        if (moveDir.sqrMagnitude > 0f)
            controller.Move(moveDir * moveSpeed * Time.deltaTime);

        // Gravedad simple
        if (controller.isGrounded && velocity.y < 0f) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}
