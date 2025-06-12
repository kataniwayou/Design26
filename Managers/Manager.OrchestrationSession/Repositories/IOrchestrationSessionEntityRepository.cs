
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Entities;
using Shared.Repositories.Interfaces;

namespace Manager.OrchestrationSession.Repositories;

public interface IOrchestrationSessionEntityRepository : IBaseRepository<OrchestrationSessionEntity>
{
    Task<IEnumerable<OrchestrationSessionEntity>> GetByVersionAsync(string version);
    Task<IEnumerable<OrchestrationSessionEntity>> GetByNameAsync(string name);
    Task<IEnumerable<OrchestrationSessionEntity>> GetByDefinitionAsync(string definition);
}
