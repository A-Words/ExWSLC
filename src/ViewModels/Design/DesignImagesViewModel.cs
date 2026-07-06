namespace ExWSLC.ViewModels.Design;

public sealed class DesignImagesViewModel : ImagesViewModel
{
    public DesignImagesViewModel() : base(DesignWorkspaceFactory.CreateWorkspace())
    {
        ImageSearchText = "ubuntu";
        SelectedImage = VisibleImages.FirstOrDefault();
    }
}
