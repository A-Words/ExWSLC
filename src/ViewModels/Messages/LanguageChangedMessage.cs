using CommunityToolkit.Mvvm.Messaging.Messages;

namespace ExWSLC.ViewModels.Messages;

public sealed class LanguageChangedMessage(string language) : ValueChangedMessage<string>(language);
