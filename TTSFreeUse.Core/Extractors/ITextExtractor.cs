using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocToSpeech.Core.Extractors
{
    public interface ITextExtractor
    {
        bool CanHandle(string fileExtension);
        Task<string> ExtractAsync(Stream fileStream);
    }
}
