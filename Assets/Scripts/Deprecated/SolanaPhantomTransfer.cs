//Transfer Solana With Phantom Wallet

using Solana.Unity.Wallet;
using Solana.Unity.SDK;
using UnityEngine;

public class SolanaPhantomTransfer : MonoBehaviour
{
    [SerializeField] private string recipientPublicKey;
    [SerializeField] private float amountToSend = 0.1f; // SOL

    public async void OnSendButtonClicked()
    {
        if (Web3.Account == null)
        {
            Debug.LogError("Wallet not connected!");
            return;
        }

        ulong lamports = (ulong)(amountToSend * 1_000_000_000);

        var result = await Web3.Instance.WalletBase.Transfer(
            new PublicKey(recipientPublicKey),
            lamports);

        if (result.WasSuccessful && !string.IsNullOrEmpty(result.Result))
        {
            Debug.Log($"✅ Transaction Success! Signature: {result.Result}");
        }
        else
        {
            Debug.LogError($"❌ Transaction Failed: {result.Reason}");
        }
    }
}

