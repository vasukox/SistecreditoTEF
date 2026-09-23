namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Lanza trabajo en segundo plano sin esperarlo, PERO sin poder tumbar el proceso.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE EXISTE
/// ─────────────────────────────────────────────────────────────────────────────
/// El patron <c>_ = AlgoAsync()</c> aparecia en cuatro lugares (la purga de datos
/// del arranque, la persistencia de la llave, el reenvio de OTP y una animacion), y
/// en los cuatro la proteccion era un <c>try/catch</c> escrito a mano DENTRO de la
/// lambda. Funciona mientras todos se acuerden; el dia que alguien agregue el
/// quinto sin el catch, una excepcion en esa tarea sube al
/// <c>SynchronizationContext</c> y se lleva la app —en una caja que puede estar
/// cobrando—.
///
/// Con esto la proteccion deja de depender de la memoria de quien escribe: el
/// unico modo de lanzar algo suelto ya la trae puesta.
///
/// NO usar para trabajo cuyo resultado importa. Si hay que saber si termino bien,
/// hay que esperarlo; esto es para lo que es genuinamente "mejor esfuerzo".
/// </summary>
public static class Fire
{
    /// <param name="tag">
    /// De donde salio, para poder ubicarlo en el log del terminal. Un fallo
    /// silencioso en una tienda solo se puede investigar si dice quien lo lanzo.
    /// </param>
    public static void AndForget(Func<Task> trabajo, string tag)
    {
        ArgumentNullException.ThrowIfNull(trabajo);

        _ = Task.Run(async () =>
        {
            try
            {
                await trabajo();
            }
            catch (OperationCanceledException)
            {
                // Cancelar es una salida normal: la pantalla se cerro, el token se
                // disparo. No es un fallo y no ensucia el log.
            }
            catch (Exception ex)
            {
                AppLogger.E(tag, "Fallo una tarea en segundo plano.", ex);
            }
        });
    }

    /// <summary>
    /// Sobrecarga para cuando ya se tiene la Task empezada (el caso de
    /// <c>_ = vm.AlgoAsync()</c>, que arranca en el hilo actual a proposito).
    /// </summary>
    public static void AndForget(Task trabajo, string tag)
    {
        ArgumentNullException.ThrowIfNull(trabajo);

        _ = trabajo.ContinueWith(
            t => AppLogger.E(tag, "Fallo una tarea en segundo plano.", t.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
