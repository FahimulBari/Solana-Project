using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DisplayInfo : MonoBehaviour
{
    public Button[] Buttons;
    public TextMeshProUGUI PublicKey;
    public TextMeshProUGUI Balance;
    public static string PlayerPublicKey;

    private void OnEnable()
    {
        if (Web3.Instance == null)
        {
            Debug.LogError("Web3 not initialized.");
            return;
        }
        Web3.OnLogin += OnLogin;
        Web3.OnBalanceChange += OnBalanceChange;
    }

    private void OnDisable()
    {
        Web3.OnLogin -= OnLogin;
        Web3.OnBalanceChange -= OnBalanceChange;
    }

    private async void OnLogin(Account account)
    {
        if (account == null || PublicKey == null) return;

        // Display public key
        PlayerPublicKey = account.PublicKey;
        PublicKey.text = $"{PlayerPublicKey.Substring(0, 4)}...{PlayerPublicKey.Substring(PlayerPublicKey.Length - 4)}";
        Debug.Log($"Logged in: {PlayerPublicKey}");

        // Enable buttons
        foreach (var btn in Buttons)
        {
            if (btn != null)
                btn.interactable = true;
        }

        // Query initial balance
        var balance = await Web3.Rpc.GetBalanceAsync(account.PublicKey);
        if (balance.WasSuccessful)
            OnBalanceChange(balance.Result.Value / 1_000_000_000.0);
    }

    private void OnBalanceChange(double balance)
    {
        if (Balance == null) return;
        Balance.text = balance.ToString("F3") + " SOL";
    }
}
