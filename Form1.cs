using System;
using System.Net;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Configuration;

namespace Tarea
{
    public partial class Form1 : Form
    {
        private TcpListener server;
        private Thread serverThread;
        private bool isRunning = false;
        private string rutaCarpetaDestino; // Para almacenar la ruta de la carpeta seleccionada
        private TcpClient cliente;
        private FileSystemWatcher fileWatcher;
        private string serverIP = ConfigurationManager.AppSettings["ServerIP"];
        private int serverPort = int.Parse(ConfigurationManager.AppSettings["ServerPort"]);
        public Form1()
        {
            InitializeComponent();
        }

        private void btnSincronizar_Click(object sender, EventArgs e)
        {
            if (isRunning)
            {
                MessageBox.Show("El servidor ya está en ejecución.");
                return;
            }

            serverThread = new Thread(IniciarServidor);
            serverThread.IsBackground = true;
            serverThread.Start();
            MessageBox.Show("Servidor iniciado.");
        }
        private void IniciarServidor()
        {
            server = new TcpListener(IPAddress.Any, serverPort);
            server.Start();
            isRunning = true;

            while (isRunning)
            {
                try
                {
                    if (!server.Pending())
                    {
                        Thread.Sleep(100); // Pequeña pausa para no bloquear el ciclo
                        continue;
                    }

                    cliente = server.AcceptTcpClient();
                    Thread clienteThread = new Thread(() => ManejarCliente(cliente)); // Crear un nuevo hilo para manejar la conexión del cliente
                    clienteThread.IsBackground = true; // Establecer el hilo como fondo para que se cierre cuando se cierre el programa
                    clienteThread.Start();
                }
                catch (Exception ex)
                {
                    if (isRunning) // Registrar errores solo si el servidor está activo
                    {
                        Invoke((Action)(() => {
                            MessageBox.Show($"Error: {ex.Message}\n");
                        }));
                    }
                }
            }

            server.Stop();
        }
        private void ManejarCliente(TcpClient cliente)
        {
            try
            {
                NetworkStream stream = cliente.GetStream();
                byte[] buffer = new byte[1024];

                while (cliente.Connected)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (message.StartsWith("DELETE:"))
                    {
                        string nombreArchivo = message.Substring(7);
                        string rutaArchivo = Path.Combine(rutaCarpetaDestino, nombreArchivo);

                        if (File.Exists(rutaArchivo))
                        {
                            File.Delete(rutaArchivo); // Eliminar archivo local
                            Invoke((Action)(() => {
                                MessageBox.Show($"Archivo eliminado: {nombreArchivo}");
                            }));
                        }
                    }
                    else
                    {
                        // Manejar archivos recibidos
                        string rutaDestino = Path.Combine(rutaCarpetaDestino, message);
                        RecibirArchivo(rutaDestino, stream);
                    }

                    // Responder al cliente
                    string respuesta = "Operacion procesada";
                    byte[] respuestabytes = Encoding.UTF8.GetBytes(respuesta);
                    stream.Write(respuestabytes, 0, respuestabytes.Length);
                }                
            }
            catch (Exception ex)
            {
                Invoke((Action)(() => {
                    MessageBox.Show($"Error al manejar cliente: {ex.Message}\n");
                }));
            }
            finally
            {
                cliente.Close();
            }
        }

        private void btnDetener_Click(object sender, EventArgs e)
        {
            DetenerServidor();

            if (serverThread != null && serverThread.IsAlive)
            {
                serverThread.Join(); // Esperar a que el hilo termine
            }

            MessageBox.Show("Servidor detenido.");
        }
        private void DetenerServidor()
        {
            isRunning = false; // Detener el ciclo principal

            try
            {
                server?.Stop(); // Detener el servidor si está activo
            }
            catch (Exception ex)
            {
                Invoke((Action)(() => {
                    MessageBox.Show($"Error al detener el servidor: {ex.Message}\n");
                }));
            }
        }

        private void btnSeleccion_Click(object sender, EventArgs e)
        {
            // Crear una nueva instancia del cuadro de diálogo para seleccionar una carpeta
            using (FolderBrowserDialog carpeta = new FolderBrowserDialog())
            {
                // Configurar propiedades si es necesario (como la ruta inicial)
                if (carpeta.ShowDialog() == DialogResult.OK)
                {
                    rutaCarpetaDestino = carpeta.SelectedPath;
                    MessageBox.Show("Carpeta seleccionada: " + rutaCarpetaDestino);

                    // Iniciar el monitoreo de la carpeta
                    IniciarMonitoreoDeCarpeta(rutaCarpetaDestino);
                }
            }
        }
        private void IniciarMonitoreoDeCarpeta(string rutaCarpeta)
        {
            fileWatcher = new FileSystemWatcher(rutaCarpeta)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                Filter = "*.*", // Filtrar por todos los archivos
                EnableRaisingEvents = true // Habilitar el monitoreo
            };

            string[] archivos = Directory.GetFiles(rutaCarpeta);
            foreach(var archivo in archivos)
            {
                EnviarArchivo(archivo);
            }


            fileWatcher.Created += (sender, e) =>
            {
                // Cuando se crea un nuevo archivo, enviarlo al servidor
                EnviarArchivo(e.FullPath);
            };

            fileWatcher.Deleted += (sender, e) =>
            {
                EnviarNotificacionEliminacion(e.Name);
            };

        }
        private void EnviarArchivo(string rutaArchivo)
        {
            try
            {
                using (TcpClient cliente = new TcpClient(serverIP, serverPort))
                using (NetworkStream Stream = cliente.GetStream())
                {
                    byte[] nombreArchivo = Encoding.UTF8.GetBytes(Path.GetFileName(rutaArchivo));
                    Stream.Write(nombreArchivo, 0, nombreArchivo.Length);

                    // Enviar el archivo
                    EnviarArchivoAlServidor(rutaArchivo, Stream);
                }                
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al enviar el archivo: {ex.Message}");
            }
        }
        private void EnviarNotificacionEliminacion(string nombreArchivo)
        {
            try
            {
                using (TcpClient cliente = new TcpClient(serverIP, serverPort))
                using (NetworkStream stream = cliente.GetStream())
                {
                    string mensaje = $"DELETE:{nombreArchivo}";
                    byte[] buffer = Encoding.UTF8.GetBytes(mensaje);
                    stream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al enviar notificación de eliminación: {ex.Message}");
            }
        }
        private void EnviarArchivoAlServidor(string rutaArchivo, NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            using (FileStream fs = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read))
            {
                int bytesRead;
                while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, bytesRead);
                }
            }
        }


        private void btnEnvio_Click(object sender, EventArgs e)
        {
            
        }
        
        private void RecibirArchivo(string rutaDestino, NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            using (FileStream fs = new FileStream(rutaDestino, FileMode.Create, FileAccess.Write))
            {
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fs.Write(buffer, 0, bytesRead);
                }
            }
        }
    }
}
