using Xunit;

// App, L y App.Devices son singletons estáticos compartidos por todo el
// proceso de test. xUnit paraleliza clases de test distintas en threads
// distintos por default; en una máquina con más núcleos (como un runner de
// CI) eso hace que dos clases que tocan ese mismo estado se pisen entre sí
// de forma no determinística. Localmente no se notaba porque este entorno
// aparentemente no llegaba a solaparlas; en CI sí. Correr todo secuencial
// es la salida simple: esta suite no es lo bastante grande como para que
// importe la velocidad.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
