using Glamaholic;
using System;
using System.Collections.Generic;

internal class TreeUtils
{
    public static TreeNode? FindNodeById(List<TreeNode> nodes, Guid id) {
        foreach (var node in nodes) {
            if (node.Id == id)
                return node;

            if (node is FolderNode folder) {
                var found = FindNodeById(folder.Children, id);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    public static (List<TreeNode>? parent, int index) FindNodeParent(List<TreeNode> rootNodes, Guid id) {
        for (int i = 0; i < rootNodes.Count; i++) {
            if (rootNodes[i].Id == id)
                return (rootNodes, i);
        }

        foreach (var node in rootNodes) {
            if (node is FolderNode folder) {
                var result = FindNodeParent(folder.Children, id);
                if (result.parent != null)
                    return result;
            }
        }

        return (null, -1);
    }

    public static bool MoveNodeToFolder(List<TreeNode> rootNodes, Guid nodeId, Guid? targetFolderId) {
        // Find the node to move
        var (sourceParent, sourceIndex) = FindNodeParent(rootNodes, nodeId);
        if (sourceParent == null || sourceIndex == -1)
            return false;

        var nodeToMove = sourceParent[sourceIndex];

        // Find target location
        List<TreeNode> targetParent;
        if (targetFolderId == null) {
            // Move to root
            targetParent = rootNodes;
        } else {
            var targetFolder = FindNodeById(rootNodes, targetFolderId.Value) as FolderNode;
            if (targetFolder == null)
                return false;
            targetParent = targetFolder.Children;
        }

        // Perform the move
        sourceParent.RemoveAt(sourceIndex);
        targetParent.Add(nodeToMove);

        return true;
    }

    public static bool RemoveNode(List<TreeNode> rootNodes, Guid id) {
        var (parent, index) = FindNodeParent(rootNodes, id);
        if (parent == null || index == -1)
            return false;

        parent.RemoveAt(index);
        return true;
    }

    public static bool DeleteFolder(List<TreeNode> rootNodes, Guid folderId, bool moveContentsToParent = false) {
        var folder = FindNodeById(rootNodes, folderId) as FolderNode;
        if (folder == null)
            return false;

        var (parent, index) = FindNodeParent(rootNodes, folderId);
        if (parent == null || index == -1)
            return false;

        if (moveContentsToParent && folder.Children.Count > 0) {
            // Insert children at the folder's position
            parent.RemoveAt(index);
            parent.InsertRange(index, folder.Children);
        } else {
            // Just remove the folder (and all contents)
            parent.RemoveAt(index);
        }

        return true;
    }

    public static List<(Guid id, PlateNode plate)> GetAllPlates(List<TreeNode> rootNodes) {
        var plates = new List<(Guid, PlateNode)>();
        CollectPlates(rootNodes, plates);
        return plates;
    }

    private static void CollectPlates(List<TreeNode> nodes, List<(Guid, PlateNode)> plates) {
        foreach (var node in nodes) {
            if (node is PlateNode plate) {
                plates.Add((node.Id, plate));
            } else if (node is FolderNode folder) {
                CollectPlates(folder.Children, plates);
            }
        }
    }

    public static bool ReplacePlate(List<TreeNode> rootNodes, Guid plateId, SavedPlate newPlate) {
        var (parent, index) = FindNodeParent(rootNodes, plateId);
        if (parent == null || index == -1 || parent[index] is not PlateNode)
            return false;

        var newNode = new PlateNode(newPlate) { Id = plateId }; // Preserve the ID
        parent[index] = newNode;
        return true;
    }

    public static int GetPlateIndex(List<TreeNode> rootNodes, Guid plateId) {
        var plates = GetAllPlates(rootNodes);
        for (int i = 0; i < plates.Count; i++) {
            if (plates[i].id == plateId)
                return i;
        }
        return -1;
    }

    public static Guid? GetPlateIdByIndex(List<TreeNode> rootNodes, int index) {
        var plates = GetAllPlates(rootNodes);
        if (index >= 0 && index < plates.Count)
            return plates[index].id;
        return null;
    }

    public static void Sort(List<TreeNode> rootNodes)
    {
        rootNodes.Sort((a, b) =>
        {
            bool aFolder = a is FolderNode;
            bool bFolder = b is FolderNode;
            if (aFolder && !bFolder) return -1;
            if (!aFolder && bFolder) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var node in rootNodes)
        {
            if (node is FolderNode folder) Sort(folder.Children);
        }
    }
}