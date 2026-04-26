using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioTempoController : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    [Header("Tempo Settings")]
    [SerializeField] private float minPitch = 0.8f;
    [SerializeField] private float maxPitch = 1.5f;

    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 5f;

    private float _targetPitch;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    public void UpdateTempo(float normalizedValue)
    {
        _targetPitch = Mathf.Lerp(minPitch, maxPitch, normalizedValue);
    }

    private void Update()
    {
        if (audioSource == null) return;

        audioSource.pitch = Mathf.Lerp(
            audioSource.pitch,
            _targetPitch,
            Time.deltaTime * smoothSpeed
        );
    }
}