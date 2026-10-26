namespace Offline3D.RevitExporter;

internal static class AutomaticPlanChoice
{
    /// <returns>The sole plan or sole selected plan's index; -1 means choose.</returns>
    internal static int SelectIndex<T>(IReadOnlyList<T> plans,ICollection<T> selected)
    {
        if(plans.Count==1)return 0;
        int index=-1;
        for(int i=0;i<plans.Count;i++)if(selected.Contains(plans[i])){
            if(index>=0)return -1;
            index=i;
        }
        return index;
    }
}
