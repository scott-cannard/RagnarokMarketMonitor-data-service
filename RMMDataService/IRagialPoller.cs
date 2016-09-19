using System.IO;
using System.ServiceModel;

namespace RMMDataService
{
    [ServiceContract(SessionMode = SessionMode.Required, CallbackContract = typeof(IServiceCallback))]
    public interface IRagialPoller
    {
        [OperationContract(IsOneWay = true)]
        void RegisterObserver(string itemName);

        [OperationContract(IsOneWay = true)]
        void UnregisterObserver(string itemName);

        [OperationContract(IsOneWay = true)]
        void SetTargetServer(string serverName);
    }


    public interface IServiceCallback
    {
        [OperationContract(IsOneWay = true)]
        void ClientUpdate(MemoryStream pushData);

        [OperationContract(IsOneWay = true)]
        void ClientErrorMessage(string msg);
    }
}
