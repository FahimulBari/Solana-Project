using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DonateUI : MonoBehaviour
{
    public TMP_InputField InputField;
    public Button SendButton;
    public SolanaTransfer solanaTransfer; // Reference to SolanaTransfer script

    private void Start()
    {
        // Attach the button listener
        SendButton.onClick.AddListener(ProcessSend);
    }

    public void ProcessSend()
    {
        if (solanaTransfer == null)
        {
            Debug.LogError("SolanaTransfer reference missing in DonateUI!");
            return;
        }

        if (string.IsNullOrEmpty(InputField.text))
        {
            Debug.LogWarning("Input field is empty.");
            return;
        }

        if (float.TryParse(InputField.text, out float amount))
        {
            if (amount <= 0)
            {
                Debug.LogWarning("Amount must be greater than 0.");
                return;
            }

            solanaTransfer.OnSendButtonClicked(amount);
        }
        else
        {
            Debug.LogWarning("Invalid input amount.");
        }
    }
}
