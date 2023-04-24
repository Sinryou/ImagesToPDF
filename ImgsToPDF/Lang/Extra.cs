using System;

namespace ImgsToPDF.Lang {
    internal class Extra {
        /// <summary>
        /// 应用资源文件中某个值
        /// </summary>
        /// <param name="resourceObject">指定用哪个界面类的资源文件</param>
        /// <param name="Name">指定用资源文件中的哪个值</param>
        /// <returns></returns>
        internal static string ApplyResource(Type resourceObject, string Name) {
            System.Resources.ResourceManager resource = new System.Resources.ResourceManager(resourceObject);
            return resource.GetString(Name);
        }
    }
}
