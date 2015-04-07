using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media.Imaging; //Namespaces a agregar
using WindowsPreview.Kinect;
using Windows.UI.Xaml.Shapes;
using Windows.UI; //Namespaces a agregar

namespace DepthInDepth
{
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Quién nos informará la cantidad de bytes por pixel
        /// </summary>
        private readonly uint bytesPerPixel=4;
        /// <summary>
        /// Kinect Activo
        /// </summary>
        private KinectSensor kinectSensor = null;
        /// <summary>
        /// Quién va a leer por los frames de color
        /// </summary>
        private DepthFrameReader depthFrameReader = null;
        /// <summary>
        /// Bitmap que se dibujará en pantalla
        /// </summary>
        private WriteableBitmap bitmap = null;
        /// <summary>
        /// Donde almacenaremos en forma la info del Depth
        /// </summary>
        private ushort[] depthFrameData = null;
        /// <summary>
        /// Donde almacenaremos en forma de bytes la info del Depth convertida a color del Kinect
        /// </summary>
        private byte[] depthPixels = null;

        public MainPage()
        {
            // get the kinectSensor object
            this.kinectSensor = KinectSensor.GetDefault();
            // get the depthFrameDescription from the DepthFrameSource
            FrameDescription depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
            // open the reader for the depth frames
            this.depthFrameReader = this.kinectSensor.DepthFrameSource.OpenReader();
            // wire handler for frame arrival
            this.depthFrameReader.FrameArrived += this.Reader_DepthFrameArrived;
            // allocate space to put the pixels being received and converted
            this.depthFrameData = new ushort[depthFrameDescription.Width * depthFrameDescription.Height];// aquí almacenamos las distancias,
            this.depthPixels = new byte[depthFrameDescription.Width * depthFrameDescription.Height * this.bytesPerPixel];//aquí almacenamos convertidos a colorBGRA

            // create the bitmap to display
            this.bitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height);
            // open the sensor
            this.kinectSensor.Open();
            // initialize the components (controls) of the window
            this.InitializeComponent();
            theImage.Source = this.bitmap;
        }

        private void Reader_DepthFrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            ushort minDepth = 0;// con esta variable vamos a controlar el rango mínimo de visión que nos interese analizar
            ushort maxDepth = 0;// con esta variable vamos a controlar el rango máximo de visión que nos interese analizar

            bool depthFrameProcessed = false;

            using (DepthFrame depthFrame = e.FrameReference.AcquireFrame())// DepthFrame es IDisposable y en el estará toda la información del frame en un respectivo tiempo (FPS)
            {
                if (depthFrame != null)
                {
                    FrameDescription depthFrameDescription = depthFrame.FrameDescription;
                    // verificando la información que hay en el frame
                    if (((depthFrameDescription.Width * depthFrameDescription.Height) == this.depthFrameData.Length) &&
                        (depthFrameDescription.Width == this.bitmap.PixelWidth) && (depthFrameDescription.Height == this.bitmap.PixelHeight))
                    {
                        depthFrame.CopyFrameDataToArray(this.depthFrameData); //Si todo va bien, entonces copiamos esa información del frame al depthFrameData, aquí toda la información es acerca de distancias

                        minDepth = 900;// establecemos el rango que nos interesa. Se coloca en milimetros.
                        maxDepth = 4000;
                        depthFrameProcessed = true;
                    }
                }
            }

            if (depthFrameProcessed)// Si ya tenemos el frame y todo cumple con lo que requerimos, vamos a convertir esas distancias en color y a pintarlas
            {
                ConvertDepthData(minDepth, maxDepth); //método para convertir las distancias de nuestro interés en color
                CreateDepthHistogram(minDepth, maxDepth, this.depthFrameData); //método para crear la visualización de la distribución de datos en Histogramas.
                this.InvalidateArrange();
                depthPixels.CopyTo(this.bitmap.PixelBuffer);
                this.bitmap.Invalidate();                
            }
        }

        private void ConvertDepthData(ushort minDepth, ushort maxDepth)
        {
            // indice del byte, se irá incrementando. Al igual que hicimos con colorSource, aquí lo haremos manualmente, crearemos nuestro arreglo de bytes
            // representando cada pixel del DepthFrameData por 4 bytes, un byte por canal (BGRA). De esta forma tendremos un arreglo de 868.352 bytes
            int colorPixelIndex = 0;

            for (int i = 0; i < this.depthFrameData.Length; ++i)// recorremos todos los 217.088 pixeles
            {
                // Obteniendo la profunidad o distancia a la que se encuentra este pixel (bueno el pixel en sí no, pero si a lo que representa)
                ushort depth = this.depthFrameData[i];
                byte intensity;//la intensidad a colocar

               //Ahora preguntemos por la información que tiene ese pixel, está en los límites que deseo analizar?
                if (depth>=minDepth && depth<=maxDepth)
                {
                    //si si, entonces por el momento vamos a representar una intensidad de color basada en esta fórmula (nada en especial, solo quiero tener diferentes intensidades según la distancia
                    //identificada. Los posibles valores arrojados en el array que entrega el kinect están entre los valores de 0 a 4500 aproximadamente, pues son los milimetros que puede ver.
                    //(aunque extendible a 8000 mm). Entonces lo que hago es dividir la distancia detectada sobre 256 que es la cantidad de valores que puede tomar un color byte, le multiplico por 10
                    // pues como para que den valores mas distanciados. Not a big deal.
                    intensity = (byte)((depth / 256) * 10);
                }
                else
                {
                    //si no, entonces vamos a representarlo con el color negro
                    intensity = 0;
                }                

                // Write out blue byte
                this.depthPixels[colorPixelIndex++] = 0;
                // Write out green byte
                this.depthPixels[colorPixelIndex++] = intensity;
                // Write out red byte                        
                this.depthPixels[colorPixelIndex++] = 0;
                // Write out alpha byte                        
                this.depthPixels[colorPixelIndex++] = 255;

                //Noten que aquí quiero que la intensidad se aplique solamente al byte que representa al Verde. Entonces todo dará en tonalidades de verde
            }
        }

        private void CreateDepthHistogram(ushort minDepth, ushort maxDepth, ushort[] pixelData)
        {
            int depth=0;
            int[] depths = new int[maxDepth+1];// la máxima cantidad de profundidades posibles, sería la del límite superior, es decir, si la maxima distancia establecida es 4000 mm,
            //los posibles valores estarán entre 0 a 4000;
            double chartBarWidth = Math.Max(3, HistogramStackPanel.ActualWidth / depths.Length);//según la cantidad de posibles barras a dibujar establecemos un ancho para crear las barras
            int maxValue = 0;

            for (int i = 0; i < pixelData.Length; ++i) //Recorremos y buscamos en cada pixel
            {
                depth = pixelData[i];
                if (depth >= minDepth && depth <= maxDepth)
                {
                    depths[depth]++;//aumentamos en 1, la frecuencia de la distancia detectada, siempre y cuando esté en los límites
                }
            }

            for (int i = minDepth; i < maxDepth; i+=10)//ahora tomaremos en cuenta las distancias detectadas, dentro del límite, pero solo nos interesará analizar por centimetro (10 mm)
            {
                maxValue = Math.Max(maxValue, depths[i]);//buscaremos cual fue la frecuencia mayor, para establecer luego la altura de las barras
            }

            HistogramStackPanel.Children.Clear();// Limpiamos el stackpanel para dibujar los nuevos datos.
            for (int i = minDepth; i < maxDepth; i+=10)// de nuevo nos enfocaremos solo en analizar centímetro a centímetro, para no recargar mucho el UI y que se congele
            {
                if (depths[i] >1)//Para dibujar solo tomaremos las distancias que hayan tenido al menos una frecuencia mayor a 1 (2 pixeles con esa distancia esta bién para efectos del ejemplo)
                {
                            HistogramStackPanel.Children.Add(new Rectangle()
                            {
                                Fill = new SolidColorBrush(Colors.Red),
                                Width = chartBarWidth,
                                Height = HistogramStackPanel.ActualHeight * (depths[i] / (double)maxValue),
                                Margin = new Thickness(1, 0, 1, 0),
                                VerticalAlignment = VerticalAlignment.Bottom
                            });
                                      
                }
            }
        }         
    }
}
