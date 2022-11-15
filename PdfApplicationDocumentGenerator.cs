using Nml.Improve.Me.Dependencies;
using System;
using System.Linq;

namespace Nml.Improve.Me
{
    public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
    {
        private readonly IDataContext _dataContext;
        private readonly IPathProvider _templatePathProvider;
        private readonly IViewGenerator _viewGenerator;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
        private readonly IPdfGenerator _pdfGenerator;

        public PdfApplicationDocumentGenerator(
            IDataContext dataContext,
            IPathProvider templatePathProvider,
            IViewGenerator viewGenerator,
            IConfiguration configuration,
            IPdfGenerator pdfGenerator,
            ILogger<PdfApplicationDocumentGenerator> logger)
        {
            _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
            _templatePathProvider = templatePathProvider ?? throw new ArgumentNullException(nameof(templatePathProvider));
            _viewGenerator = viewGenerator ?? throw new ArgumentNullException(nameof(viewGenerator));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pdfGenerator = pdfGenerator ?? throw new ArgumentNullException(nameof(pdfGenerator));
        }

        public byte[] Generate(Guid applicationId, string baseUri)
        {
            var application = _dataContext.Applications.FirstOrDefault(a => a.Id == applicationId);

            if (application == null)
            {
                var errorMessage = $"No application found for id '{applicationId}'";
                _logger.LogWarning(errorMessage);
                throw new ArgumentNullException(errorMessage);
            }

            if (baseUri.EndsWith("/"))
                baseUri = baseUri.Substring(baseUri.Length - 1);


            var view = GenerateView(application, baseUri);

            if (view == null)
            {
                var errorMessage = $"The application is in state '{application.State}' and no valid document can be generated for it.";
                _logger.LogWarning(errorMessage);
                throw new Exception(errorMessage);
            }

            var pdfOptions = GetPdfOptions();

            var pdf = _pdfGenerator.GenerateFromHtml(view, pdfOptions);
            return pdf.ToBytes();
        }

        private string GenerateView(Application application, string baseUri)
        {
            var path = string.Empty;

            switch (application.State)
            {
                case ApplicationState.Pending:
                    path = _templatePathProvider.Get("PendingApplication");

                    var pendingApplicationViewModel = new PendingApplicationViewModel
                    {
                        ReferenceNumber = application.ReferenceNumber,
                        State = application.State.ToDescription(),
                        FullName = GetPersonFullName(application),
                        AppliedOn = application.Date,
                        SupportEmail = _configuration.SupportEmail,
                        Signature = _configuration.Signature
                    };

                    return _viewGenerator.GenerateFromPath($"{baseUri}{path}", pendingApplicationViewModel);

                case ApplicationState.Activated:
                    path = _templatePathProvider.Get("ActivatedApplication");

                    var activatedApplicationViewModel = new ActivatedApplicationViewModel
                    {
                        ReferenceNumber = application.ReferenceNumber,
                        State = application.State.ToDescription(),
                        FullName = GetPersonFullName(application),
                        LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                        PortfolioFunds = application.Products.SelectMany(p => p.Funds).ToList(),
                        PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
                                                .Sum(f => (f.Amount - f.Fees) * _configuration.TaxRate),
                        AppliedOn = application.Date,
                        SupportEmail = _configuration.SupportEmail,
                        Signature = _configuration.Signature
                    };

                    return _viewGenerator.GenerateFromPath($"{baseUri}{path}", activatedApplicationViewModel);

                case ApplicationState.InReview:
                    path = _templatePathProvider.Get("InReviewApplication");

                    var inReviewApplicationViewModel = new InReviewApplicationViewModel
                    {

                        ReferenceNumber = application.ReferenceNumber,
                        State = application.State.ToDescription(),
                        FullName = GetPersonFullName(application),
                        LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
                        PortfolioFunds = application.Products.SelectMany(p => p.Funds).ToList(),
                        PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
                                                .Sum(f => (f.Amount - f.Fees) * _configuration.TaxRate),
                        InReviewMessage = GetInReviewMessage(application),
                        InReviewInformation = application.CurrentReview,
                        AppliedOn = application.Date,
                        SupportEmail = _configuration.SupportEmail,
                        Signature = _configuration.Signature
                    };

                    return _viewGenerator.GenerateFromPath($"{baseUri}{path}", inReviewApplicationViewModel);

                default:
                    return null;
            }
        }

        private PdfOptions GetPdfOptions()
        {
            return new PdfOptions
            {
                PageNumbers = PageNumbers.Numeric,
                HeaderOptions = new HeaderOptions
                {
                    HeaderRepeat = HeaderRepeat.FirstPageOnly,
                    HeaderHtml = PdfConstants.Header
                }
            };
        }

        private string GetPersonFullName(Application application)
        {
            return $"{application.Person.FirstName} {application.Person.Surname}";
        }

        private string GetInReviewMessage(Application application)
        {
            var inReviewMessage = application.CurrentReview.Reason.Contains("address") ? 
                    " pending outstanding address verification for FICA purposes.": 
                        application.CurrentReview.Reason.Contains("bank") ? 
                             " pending outstanding bank account verification." : 
                                " because of suspicious account behaviour. Please contact support ASAP.";

            return "Your application has been placed in review" + inReviewMessage;
        }
    }
}
