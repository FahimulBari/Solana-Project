using UnityEngine;

[CreateAssetMenu(fileName = "NewStoreItem", menuName = "Solana/StoreItemData")]
public class StoreItemData : ScriptableObject
{
    public string itemName;
    public string description;
    public Sprite icon;
    public float priceInSOL;

    [Header("Minting Metadata")]
    public string metadataUri; 
}
