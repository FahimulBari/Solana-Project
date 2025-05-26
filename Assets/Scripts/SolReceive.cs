using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Solana.Unity.Programs;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Wallet.Utilities;
using TMPro;
using UnityEngine;

public class SolReceive : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI Debugging;

    [Header("Transaction Settings")]
    [Tooltip("Amount in SOL to transfer (0.001 = 1,000,000 lamports)")]
    public float transferAmount = 0.001f;

    [Tooltip("Maximum transaction time in seconds")]
    [Range(10, 60)]
    public int transactionTimeout = 30;

    [Tooltip("Cooldown between transaction attempts")]
    [Range(1, 10)]
    public float transactionCooldown = 3f;

    private byte[] privateKeyBytes = new byte[] {
        7, 78, 9, 118, 55, 67, 216, 195, 221, 138, 16, 83, 2, 143, 65, 118,
        13, 195, 130, 249, 16, 206, 99, 19, 36, 68, 70, 29, 136, 71, 58, 148,
        92, 43, 28, 46, 176, 53, 56, 75, 126, 141, 65, 124, 253, 24, 33, 8,
        214, 226, 163, 33, 133, 46, 67, 33, 230, 137, 180, 84, 101, 251, 198, 36
    }; // Replace with actual key

    private string publicKeyBase58 = "7Cndq42N3bUoEQxFZToevwuo7iZt6HchYcLGhJaRMmVV";
    private Account sourceAccount;
    private IRpcClient rpcClient;

    // Transaction State Management
    private bool isTransactionInProgress = false;
    private float lastTransactionTime = 0f;

    private void Awake()
    {
        // Ensure Debugging text is never null
        if (Debugging == null)
        {
            GameObject debugTextObj = new GameObject("DebugText");
            Debugging = debugTextObj.AddComponent<TextMeshProUGUI>();
            debugTextObj.transform.SetParent(transform, false);
        }
    }

    private void Start()
    {
        SafeDebugLog("Initializing SolReceive script...");

        try
        {
            // Initialize RPC Client
            rpcClient = ClientFactory.GetClient(Web3.Rpc?.NodeAddress?.ToString() ?? "https://api.devnet.solana.com");

            // Initialize Source Account
            string privateKeyBase58 = Encoders.Base58.EncodeData(privateKeyBytes);
            sourceAccount = new Account(privateKeyBase58, publicKeyBase58);

            SafeDebugLog($"Backend wallet initialized: {FormatPublicKey(sourceAccount.PublicKey)}", Color.green);

            // Check initial balance
            CheckSourceAccountBalance();
        }
        catch (System.Exception ex)
        {
            SafeDebugLog($"Initialization failed: {ex.Message}", Color.red);
        }
    }

    // Safe method to log and update debug text
    private void SafeDebugLog(string message, Color? textColor = null)
    {
        // Ensure we always have a Debugging component
        if (Debugging == null)
        {
            Debug.LogError($"Debug Text is null. Message: {message}");
            return;
        }

        // Update text color if specified
        if (textColor.HasValue)
        {
            Debugging.color = textColor.Value;
        }
        else
        {
            Debugging.color = Color.white;
        }

        // Update debug text
        Debugging.text = message;

        // Log to console
        Debug.Log(message);
    }

    // Format public key for display
    private string FormatPublicKey(PublicKey publicKey)
    {
        if (publicKey == null) return "N/A";
        string keyStr = publicKey.ToString();
        return $"{keyStr.Substring(0, 4)}...{keyStr.Substring(keyStr.Length - 4)}";
    }

    // Check source account balance
    private async void CheckSourceAccountBalance()
    {
        if (sourceAccount == null)
        {
            SafeDebugLog("Cannot check balance: Source account not initialized", Color.yellow);
            return;
        }

        try
        {
            var balance = await rpcClient.GetBalanceAsync(sourceAccount.PublicKey);
            if (balance.WasSuccessful)
            {
                double solBalance = balance.Result.Value / 1_000_000_000.0;
                SafeDebugLog($"Backend wallet balance: {solBalance:F4} SOL",
                    solBalance < 0.01 ? Color.yellow : Color.green);
            }
            else
            {
                SafeDebugLog($"Balance check failed: {balance.Reason}", Color.red);
            }
        }
        catch (System.Exception ex)
        {
            SafeDebugLog($"Balance check error: {ex.Message}", Color.red);
        }
    }

    // Public method to initiate SOL transfer
    public void GainSol()
    {
        // Check if a transaction is in progress or cooldown is active
        if (isTransactionInProgress)
        {
            SafeDebugLog("Transaction in progress. Please wait.", Color.yellow);
            return;
        }

        if (Time.time - lastTransactionTime < transactionCooldown)
        {
            SafeDebugLog($"Cooldown active. Wait {transactionCooldown} seconds between transfers.", Color.yellow);
            return;
        }

        // Start the transfer process
        StartCoroutine(TransferSolCoroutine());
    }

    // Coroutine to handle SOL transfer with timeout
    private IEnumerator TransferSolCoroutine()
    {
        // Prevent multiple simultaneous transfers
        isTransactionInProgress = true;
        SafeDebugLog("Preparing SOL transfer...", Color.blue);

        // Validate player wallet
        if (string.IsNullOrEmpty(DisplayInfo.PlayerPublicKey))
        {
            SafeDebugLog("Error: Player wallet not connected.", Color.red);
            isTransactionInProgress = false;
            yield break;
        }

        // Track start time for timeout
        float startTime = Time.time;

        // Create task for transfer
        Task<bool> transferTask = TransferSol();

        // Wait for transfer to complete or timeout
        while (!transferTask.IsCompleted)
        {
            // Check for timeout
            if (Time.time - startTime > transactionTimeout)
            {
                SafeDebugLog("Transfer timed out.", Color.red);
                isTransactionInProgress = false;
                yield break;
            }

            yield return null;
        }

        // Check transfer result
        bool transferResult = false;
        try
        {
            transferResult = transferTask.Result;
        }
        catch (System.Exception ex)
        {
            SafeDebugLog($"Transfer exception: {ex.Message}", Color.red);
        }

        // Update state and time
        isTransactionInProgress = false;
        lastTransactionTime = Time.time;

        // Final status update
        if (transferResult)
        {
            SafeDebugLog("SOL transfer successful!", Color.green);
        }
        else
        {
            SafeDebugLog("SOL transfer failed.", Color.red);
        }
    }

    // Async method to perform the actual transfer
    private async Task<bool> TransferSol()
    {
        try
        {
            // Validate source account and destination
            if (sourceAccount == null)
            {
                SafeDebugLog("Backend wallet not initialized.", Color.red);
                return false;
            }

            if (!PublicKey.IsValid(DisplayInfo.PlayerPublicKey))
            {
                SafeDebugLog("Invalid player wallet address.", Color.red);
                return false;
            }

            var destinationPublicKey = new PublicKey(DisplayInfo.PlayerPublicKey);
            SafeDebugLog($"Sending SOL to: {FormatPublicKey(destinationPublicKey)}", Color.blue);

            // Convert transfer amount to lamports
            ulong lamports = (ulong)(transferAmount * 1_000_000_000);
            if (lamports == 0)
            {
                SafeDebugLog("Transfer amount too small.", Color.red);
                return false;
            }

            // Check backend wallet balance
            var balance = await rpcClient.GetBalanceAsync(sourceAccount.PublicKey, Commitment.Confirmed);
            if (!balance.WasSuccessful)
            {
                SafeDebugLog($"Balance check failed: {balance.Reason}", Color.red);
                return false;
            }

            if (balance.Result.Value < (lamports + 5000))
            {
                SafeDebugLog($"Insufficient balance. Required: {lamports}, Available: {balance.Result.Value}", Color.red);
                return false;
            }

            // Get latest blockhash
            var blockHash = await rpcClient.GetLatestBlockHashAsync(Commitment.Confirmed);
            if (!blockHash.WasSuccessful)
            {
                SafeDebugLog($"Blockhash retrieval failed: {blockHash.Reason}", Color.red);
                return false;
            }

            // Build transaction
            var transaction = new TransactionBuilder()
                .SetRecentBlockHash(blockHash.Result.Value.Blockhash)
                .SetFeePayer(sourceAccount.PublicKey)
                .AddInstruction(SystemProgram.Transfer(
                    fromPublicKey: sourceAccount.PublicKey,
                    toPublicKey: destinationPublicKey,
                    lamports: lamports))
                .Build(new[] { sourceAccount });

            SafeDebugLog("Sending reward...", Color.blue);

            // Send transaction
            var txSig = await rpcClient.SendTransactionAsync(transaction, commitment: Commitment.Confirmed);

            if (txSig.WasSuccessful)
            {
                string shortTxId = $"{txSig.Result.Substring(0, 8)}...{txSig.Result.Substring(txSig.Result.Length - 8)}";
                SafeDebugLog($"Transfer successful: {transferAmount} SOL sent. TX: {shortTxId}", Color.green);

                // Optional: Update wallet balance
                if (Web3.Instance?.WalletBase?.Account != null)
                {
                    await Web3.UpdateBalance();
                }

                return true;
            }
            else
            {
                SafeDebugLog($"Transfer failed: {txSig.Reason}", Color.red);
                return false;
            }
        }
        catch (System.Exception ex)
        {
            SafeDebugLog($"Unexpected transfer error: {ex.Message}", Color.red);
            return false;
        }
    }
}