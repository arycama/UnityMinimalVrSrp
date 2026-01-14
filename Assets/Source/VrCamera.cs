using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.XR;
using static UnityEngine.Mathf;

public class VrCamera : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float keyboardLookSpeed = 1f;
    [SerializeField] private float mouseLookSpeed = 0.01f;
    [SerializeField] private float rollSpeed = 1f;

    private float pitch, yaw, roll;
    private Vector3 offset;

    void Update()
    {
        float GetAxis(KeyCode negative, KeyCode positive)
        {
            var neg = Input.GetKey(negative);
            return neg == Input.GetKey(positive) ? 0 : neg ? -1 : 1;
        }

        // Mouse/keyboard input
        var sway = GetAxis(KeyCode.A, KeyCode.D);
        var heave = GetAxis(KeyCode.Space, KeyCode.C);
        var surge = GetAxis(KeyCode.S, KeyCode.W);
        var translation = moveSpeed * Time.deltaTime * new Vector3(sway, heave, surge);
        offset += translation;

        var yawInput = -GetAxis(KeyCode.LeftArrow, KeyCode.RightArrow) * keyboardLookSpeed * Time.deltaTime;
        var pitchInput = -GetAxis(KeyCode.DownArrow, KeyCode.UpArrow) * keyboardLookSpeed * Time.deltaTime;
        var rollInput = -GetAxis(KeyCode.Q, KeyCode.E) * rollSpeed * Time.deltaTime;

        // Mouse Input
        yawInput += Input.GetAxis("Mouse X") * mouseLookSpeed;
        pitchInput -= Input.GetAxis("Mouse Y") * mouseLookSpeed;

        yaw = Repeat(yaw + yawInput, PI);
        pitch = Clamp(pitch + pitchInput, -0.25f * PI, 0.25f * PI);
        roll = Clamp(roll + rollInput, -0.25f * PI, 0.25f * PI);

        // Reset to current XR position/rotation
        if (Input.GetKeyDown(KeyCode.R))
        {
            pitch = yaw = roll = 0;
            offset = Vector3.zero;
        }

        void SinCos(float angle, out float sinAngle, out float cosAngle)
        {
            sinAngle = Sin(angle);
            cosAngle = Cos(angle);
        }

        SinCos(yaw, out var st, out var ct);
        SinCos(pitch, out var sp, out var cp);

        // Roll. not currently used since it's a bit tricky to make work with seperate pitch/yaw and XR input
        //SinCos(roll, out var sr, out var cr);
        // var rollRot = new Quaternion(0, 0, sr, cr);  // rotation around Z by roll

        var position = Vector3.zero;// offset;
        var rotation = Quaternion.Identity;// new Quaternion(ct * sp, st * cp, -st * sp, ct * cp);

        // XR Input
        using (ListPool<XRNodeState>.Get(out var nodeStates))
        {
            InputTracking.GetNodeStates(nodeStates);
            foreach (var nodeState in nodeStates)
            {
                if (nodeState.nodeType != XRNode.Head)
                    continue;

                if (nodeState.TryGetPosition(out var xrPosition))
                    position += xrPosition;

                if (nodeState.TryGetRotation(out var xrRotation))
                    rotation *= xrRotation;
            }
        }

        transform.SetLocalPositionAndRotation(position, rotation);
    }
}