using StarterAssets;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class JumpPad : MonoBehaviour
{
    [Tooltip("Wie stark der Spieler nach oben geschossen wird")]
    [SerializeField]
    public float jumpForce = 15f;

    [Tooltip("Optional: Sound oder Effekt beim Auslösen")]
    public ParticleSystem jumpEffect;

    public void ActivatePad(FirstPersonController player)
    {
        player.ApplyJumpPadForce(jumpForce);
    }
}